using PrepHub.Application.Abstractions;
using PrepHub.Application.Contracts;
using PrepHub.Domain.Autenticacao;
using PrepHub.Domain.Entidades;
using PrepHub.Domain.Enums;

namespace PrepHub.Application.Autenticacao;

public sealed class AutenticacaoService : IAutenticacaoService
{
    private readonly IUsuarioRepository _usuarios;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IHasherDeSenha _hasher;
    private readonly IGeradorDeTokenSeguro _tokens;
    private readonly IMetricasDeNegocio _metricas;

    /// <summary>
    /// Hash descartável usado quando o e-mail não existe. Sem ele, "e-mail inexistente"
    /// responderia na hora e "senha errada" gastaria o custo do PBKDF2 — a diferença de tempo
    /// entrega quem tem conta no sistema. Lazy porque só o caminho de falha precisa dele.
    /// </summary>
    private readonly Lazy<string> _hashDeReferencia;

    public AutenticacaoService(
        IUsuarioRepository usuarios,
        IUnitOfWork unitOfWork,
        IClock clock,
        IHasherDeSenha hasher,
        IGeradorDeTokenSeguro tokens,
        IMetricasDeNegocio metricas)
    {
        _usuarios = usuarios;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _hasher = hasher;
        _tokens = tokens;
        _metricas = metricas;
        _hashDeReferencia = new Lazy<string>(() => _hasher.Hash("senha-de-referencia-sem-uso"));
    }

    public async Task<UsuarioDto> ObterOuCriarAsync(
        LoginExternoRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.UtcNow;
        var usuario = await _usuarios.ObterPorProvedorAsync(request.Provider, request.ProviderKey, cancellationToken);
        var contaNova = usuario is null;

        if (usuario is null)
        {
            // Primeiro login: o nome pode vir vazio (GitHub sem nome público) — o handle do
            // provedor já foi resolvido pelo Web, mas garantimos um rótulo utilizável aqui.
            var name = string.IsNullOrWhiteSpace(request.Name) ? "Candidato" : request.Name;

            usuario = new Usuario(
                request.Provider,
                request.ProviderKey,
                name,
                request.Email,
                request.AvatarUrl,
                now);

            await _usuarios.AdicionarAsync(usuario, cancellationToken);
        }
        else
        {
            usuario.RegistrarLogin(request.Name, request.Email, request.AvatarUrl, now);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Só depois do SaveChanges: contar antes registraria como cadastro o que uma violação de
        // índice único ainda pode desfazer.
        if (contaNova)
        {
            _metricas.ContaCriada(request.Provider);
        }

        // No login externo não existe "senha errada": ou o provedor autenticou, ou o fluxo nem
        // chega aqui (o callback devolve para a tela de login).
        _metricas.LoginRegistrado(request.Provider, ResultadoDeLogin.Sucesso);

        return Mapear(usuario);
    }

    public async Task<ResultadoDeCadastro> CadastrarComSenhaAsync(
        CadastroLocalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A política também é aplicada na tela; repetir aqui é o que impede um POST direto,
        // sem passar pelo formulário, de criar conta com senha de dois caracteres.
        if (!PoliticaDeSenha.EhAceitavel(request.Password))
        {
            _metricas.CadastroRecusado(MotivoDeRecusaDeCadastro.SenhaInaceitavel);
            return ResultadoDeCadastro.Recusado(FalhaDeAutenticacao.SenhaInaceitavel);
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Name))
        {
            _metricas.CadastroRecusado(MotivoDeRecusaDeCadastro.DadosIncompletos);
            return ResultadoDeCadastro.Recusado(FalhaDeAutenticacao.CredenciaisInvalidas);
        }

        var email = Usuario.NormalizarEmail(request.Email);

        // Duplicata é checada só dentro do provedor Local, e não contra todos os e-mails: a
        // chave da identidade continua sendo (provedor, chave), então um e-mail que também
        // aparece numa conta Google segue sendo outro usuário — exatamente como já vale hoje
        // entre Google e GitHub. Unificar contas é recurso à parte.
        var existente = await _usuarios.ObterPorProvedorAsync(ProvedorDeLogin.Local, email, cancellationToken);
        if (existente is not null)
        {
            _metricas.CadastroRecusado(MotivoDeRecusaDeCadastro.EmailJaCadastrado);
            return ResultadoDeCadastro.Recusado(FalhaDeAutenticacao.EmailJaCadastrado);
        }

        var agora = _clock.UtcNow;
        var usuario = Usuario.CriarComSenha(
            request.Name,
            email,
            _hasher.Hash(request.Password),
            agora);

        // Conta e link nascem na MESMA transação. Gravar a conta primeiro e o token depois abriria
        // a janela em que uma falha deixa a pessoa cadastrada, sem acesso e sem link nenhum para
        // abrir — e sem poder cadastrar de novo, porque o e-mail já estaria ocupado.
        var token = _tokens.Gerar();

        await _usuarios.AdicionarAsync(usuario, cancellationToken);
        await _usuarios.AdicionarTokenDeConfirmacaoAsync(
            new TokenDeConfirmacaoDeEmail(usuario.Id, _tokens.Hash(token), agora),
            cancellationToken);

        // Em corrida (dois cadastros simultâneos do mesmo e-mail) a checagem acima passa nas
        // duas e quem garante unicidade é o índice único do banco, que faz o SaveChanges
        // falhar. A checagem existe pela mensagem decente, não pela integridade.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _metricas.ContaCriada(ProvedorDeLogin.Local);
        _metricas.ConfirmacaoDeEmail(EtapaDeConfirmacaoDeEmail.LinkEmitido);

        // O e-mail informado (não o do usuário) seria idêntico aqui, mas usamos o normalizado
        // que ficou gravado — é para ele que a confirmação tem de ir.
        return ResultadoDeCadastro.Sucesso(
            Mapear(usuario),
            new TokenDeConfirmacaoDto(token, usuario.Email!, usuario.Name));
    }

    public async Task<ResultadoDeAutenticacao> AutenticarComSenhaAsync(
        LoginLocalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ⚠️ A métrica registrada em cada saída abaixo distingue casos que a TELA nunca distingue
        // (e-mail sem conta, senha errada, conta bloqueada) — é essa distinção que permite ver um
        // ataque de força bruta no painel. Não vaza nada: o rótulo é um enum de quatro valores,
        // sem e-mail nem id, e o painel é interno. O custo de contar é de microssegundos contra os
        // ~200ms do PBKDF2, então também não abre canal de tempo.
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            _metricas.LoginRegistrado(ProvedorDeLogin.Local, ResultadoDeLogin.PedidoInvalido);
            return ResultadoDeAutenticacao.Recusado(FalhaDeAutenticacao.CredenciaisInvalidas);
        }

        // Só o teto da política vale no login. Barrar por comprimento MÍNIMO aqui trancaria
        // fora quem cadastrou antes de a regra endurecer; o teto, ao contrário, evita gastar
        // PBKDF2 sobre um payload gigante enviado de propósito.
        if (request.Password.Length > PoliticaDeSenha.TamanhoMaximo)
        {
            _metricas.LoginRegistrado(ProvedorDeLogin.Local, ResultadoDeLogin.PedidoInvalido);
            return ResultadoDeAutenticacao.Recusado(FalhaDeAutenticacao.CredenciaisInvalidas);
        }

        var agora = _clock.UtcNow;
        var email = Usuario.NormalizarEmail(request.Email);
        var usuario = await _usuarios.ObterPorProvedorAsync(ProvedorDeLogin.Local, email, cancellationToken);

        // Conta social cai aqui como "não encontrada", porque a busca é dentro do provedor
        // Local — e PasswordHash nulo é a segunda barreira, caso algum dia um Local chegue
        // sem hash. Nos dois casos a resposta é a mesma do e-mail inexistente.
        if (usuario?.PasswordHash is null)
        {
            _hasher.Verificar(request.Password, _hashDeReferencia.Value);
            _metricas.LoginRegistrado(ProvedorDeLogin.Local, ResultadoDeLogin.ContaInexistente);
            return ResultadoDeAutenticacao.Recusado(FalhaDeAutenticacao.CredenciaisInvalidas);
        }

        // Conta bloqueada nem chega a conferir a senha — nem a certa entra, que é o ponto do
        // bloqueio. E ainda gasta a verificação de referência: responder mais rápido aqui
        // avisaria, pelo tempo, que a conta existe E está sob ataque.
        if (usuario.EstaBloqueada(agora))
        {
            _hasher.Verificar(request.Password, _hashDeReferencia.Value);
            _metricas.LoginRegistrado(ProvedorDeLogin.Local, ResultadoDeLogin.ContaBloqueada);
            return ResultadoDeAutenticacao.Recusado(FalhaDeAutenticacao.CredenciaisInvalidas);
        }

        if (!_hasher.Verificar(request.Password, usuario.PasswordHash))
        {
            usuario.RegistrarFalhaDeLogin(agora);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _metricas.LoginRegistrado(ProvedorDeLogin.Local, ResultadoDeLogin.SenhaIncorreta);

            // Mesma falha do caso bloqueado: a tela não diferencia "senha errada" de "conta
            // travada", senão o bloqueio viraria confirmação de que o e-mail tem conta.
            return ResultadoDeAutenticacao.Recusado(FalhaDeAutenticacao.CredenciaisInvalidas);
        }

        // ⚠️ ORDEM: a confirmação é conferida DEPOIS do hash, e isso é a regra, não acaso. Quem
        // chega até aqui já provou saber a senha, então dizer "confirme seu e-mail" não revela
        // nada que essa pessoa não soubesse. Movida para antes da verificação, esta mesma
        // checagem viraria um oráculo: bastaria enviar qualquer senha para descobrir se o
        // endereço tem cadastro — exatamente o que CredenciaisInvalidas existe para impedir.
        if (!usuario.EmailConfirmado)
        {
            // Sem RegistrarLoginLocal: a senha estava certa, mas não houve login. Carimbar o
            // acesso aqui contaria como "usuário ativo" quem nunca conseguiu entrar.
            _metricas.LoginRegistrado(ProvedorDeLogin.Local, ResultadoDeLogin.EmailNaoConfirmado);
            return ResultadoDeAutenticacao.Recusado(FalhaDeAutenticacao.EmailNaoConfirmado);
        }

        usuario.RegistrarLoginLocal(agora);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _metricas.LoginRegistrado(ProvedorDeLogin.Local, ResultadoDeLogin.Sucesso);

        return ResultadoDeAutenticacao.Sucesso(Mapear(usuario));
    }

    public async Task<TokenDeConfirmacaoDto?> ReenviarConfirmacaoDeEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        // Contado ANTES da busca, como em SolicitarRedefinicaoDeSenhaAsync: a distância entre
        // este número e LinkEmitido é o que denuncia alguém varrendo endereços por aqui.
        _metricas.ConfirmacaoDeEmail(EtapaDeConfirmacaoDeEmail.ReenvioSolicitado);

        var agora = _clock.UtcNow;
        var normalizado = Usuario.NormalizarEmail(email);
        var usuario = await _usuarios.ObterPorProvedorAsync(ProvedorDeLogin.Local, normalizado, cancellationToken);

        // Três casos caem no mesmo null: não há conta local, a conta já está confirmada (não há
        // o que reenviar), ou é conta social (que nasce confirmada). O Web responde igual aos
        // três e ao caso de sucesso — reenviar link para quem já confirmou seria, além de inútil,
        // uma forma de confirmar cadastro alheio a quem só sabe digitar e-mails.
        if (usuario is null || usuario.EmailConfirmado || usuario.Email is null)
        {
            return null;
        }

        // Um link novo mata o anterior. Dois links vivos ao mesmo tempo não ajudam ninguém e
        // ainda deixam a pessoa clicando no e-mail antigo, que é o que ela vê primeiro na caixa.
        foreach (var anterior in await _usuarios.ObterTokensDeConfirmacaoAtivosDoUsuarioAsync(usuario.Id, cancellationToken))
        {
            anterior.Invalidar(agora);
        }

        var token = _tokens.Gerar();
        await _usuarios.AdicionarTokenDeConfirmacaoAsync(
            new TokenDeConfirmacaoDeEmail(usuario.Id, _tokens.Hash(token), agora),
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _metricas.ConfirmacaoDeEmail(EtapaDeConfirmacaoDeEmail.LinkEmitido);

        return new TokenDeConfirmacaoDto(token, usuario.Email, usuario.Name);
    }

    public async Task<ResultadoDeConfirmacao> ConfirmarEmailAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            _metricas.ConfirmacaoDeEmail(EtapaDeConfirmacaoDeEmail.TokenRecusado);
            return ResultadoDeConfirmacao.TokenInvalido;
        }

        var agora = _clock.UtcNow;
        var registro = await _usuarios.ObterTokenDeConfirmacaoAsync(_tokens.Hash(token), cancellationToken);

        if (registro is null)
        {
            _metricas.ConfirmacaoDeEmail(EtapaDeConfirmacaoDeEmail.TokenRecusado);
            return ResultadoDeConfirmacao.TokenInvalido;
        }

        var usuario = await _usuarios.ObterPorIdParaAtualizacaoAsync(registro.UserId, cancellationToken);
        if (usuario is null || !usuario.EhContaLocal)
        {
            _metricas.ConfirmacaoDeEmail(EtapaDeConfirmacaoDeEmail.TokenRecusado);
            return ResultadoDeConfirmacao.TokenInvalido;
        }

        // Conta já confirmada é sucesso, não erro — e é o caso mais comum de "link inválido"
        // que não deveria ser: clique duplo, o "abrir de novo" do cliente de e-mail, ou o
        // varredor de links do provedor de destino, que abre a URL antes da pessoa. Tratar isso
        // como falha mandaria de volta para a tela de erro quem já está pronto para entrar.
        if (usuario.EmailConfirmado)
        {
            return ResultadoDeConfirmacao.JaConfirmado;
        }

        if (!registro.EstaUtilizavel(agora))
        {
            _metricas.ConfirmacaoDeEmail(EtapaDeConfirmacaoDeEmail.TokenRecusado);
            return ResultadoDeConfirmacao.TokenInvalido;
        }

        usuario.ConfirmarEmail(agora);
        registro.Consumir(agora);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _metricas.ConfirmacaoDeEmail(EtapaDeConfirmacaoDeEmail.Concluida);

        return ResultadoDeConfirmacao.Confirmado;
    }

    public async Task<TokenDeRedefinicaoDto?> SolicitarRedefinicaoDeSenhaAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        // Contado ANTES de saber se existe conta: a diferença entre "pedidos" e "links emitidos"
        // é justamente o que mostra alguém varrendo e-mails à procura de quem tem cadastro aqui.
        _metricas.RedefinicaoDeSenha(EtapaDeRedefinicao.Solicitada);

        var agora = _clock.UtcNow;
        var normalizado = Usuario.NormalizarEmail(email);
        var usuario = await _usuarios.ObterPorProvedorAsync(ProvedorDeLogin.Local, normalizado, cancellationToken);

        // Sem conta local com esse e-mail não há o que redefinir — e quem entra por Google não
        // tem senha nossa. Nos dois casos devolvemos null e o Web responde igual ao caso de
        // sucesso, para a tela não revelar quem está cadastrado.
        if (usuario?.Email is null)
        {
            return null;
        }

        // Pedir um link novo invalida os anteriores: dois links vivos ao mesmo tempo dobram a
        // janela de exposição sem servir para nada.
        foreach (var anterior in await _usuarios.ObterTokensAtivosDoUsuarioAsync(usuario.Id, cancellationToken))
        {
            anterior.Invalidar(agora);
        }

        var token = _tokens.Gerar();
        await _usuarios.AdicionarTokenDeRedefinicaoAsync(
            new TokenDeRedefinicaoDeSenha(usuario.Id, _tokens.Hash(token), agora),
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _metricas.RedefinicaoDeSenha(EtapaDeRedefinicao.LinkEmitido);

        // O token em texto sai daqui e não volta: o Web monta o link, manda o e-mail e
        // esquece. No banco ficou só o hash.
        return new TokenDeRedefinicaoDto(token, usuario.Email, usuario.Name);
    }

    public async Task<bool> TokenDeRedefinicaoEstaValidoAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var registro = await _usuarios.ObterTokenDeRedefinicaoAsync(_tokens.Hash(token), cancellationToken);
        return registro is not null && registro.EstaUtilizavel(_clock.UtcNow);
    }

    public async Task<ResultadoDeAutenticacao> RedefinirSenhaAsync(
        RedefinicaoDeSenhaRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return ResultadoDeAutenticacao.Recusado(FalhaDeAutenticacao.TokenInvalido);
        }

        if (!PoliticaDeSenha.EhAceitavel(request.NovaSenha))
        {
            return ResultadoDeAutenticacao.Recusado(FalhaDeAutenticacao.SenhaInaceitavel);
        }

        var agora = _clock.UtcNow;
        var token = await _usuarios.ObterTokenDeRedefinicaoAsync(
            _tokens.Hash(request.Token),
            cancellationToken);

        if (token is null || !token.EstaUtilizavel(agora))
        {
            return ResultadoDeAutenticacao.Recusado(FalhaDeAutenticacao.TokenInvalido);
        }

        // Rastreado, porque a senha vai ser alterada — ObterPorIdAsync é no-tracking e a
        // mudança não seria persistida.
        var usuario = await _usuarios.ObterPorIdParaAtualizacaoAsync(token.UserId, cancellationToken);
        if (usuario is null || !usuario.EhContaLocal)
        {
            return ResultadoDeAutenticacao.Recusado(FalhaDeAutenticacao.TokenInvalido);
        }

        var ativos = await _usuarios.ObterTokensAtivosDoUsuarioAsync(usuario.Id, cancellationToken);

        usuario.DefinirNovaSenha(_hasher.Hash(request.NovaSenha), agora);
        token.Consumir(agora);

        // Trocar a senha derruba os outros links pendentes: quem pediu dois e-mails não deve
        // deixar um deles valendo depois de a senha já ter mudado.
        foreach (var irmao in ativos.Where(t => t.Id != token.Id))
        {
            irmao.Invalidar(agora);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _metricas.RedefinicaoDeSenha(EtapaDeRedefinicao.Concluida);

        return ResultadoDeAutenticacao.Sucesso(Mapear(usuario));
    }

    public async Task<UsuarioDto?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var usuario = await _usuarios.ObterPorIdAsync(id, cancellationToken);
        return usuario is null ? null : Mapear(usuario);
    }

    private static UsuarioDto Mapear(Usuario usuario)
        => new(usuario.Id, usuario.Name, usuario.Email, usuario.AvatarUrl, usuario.Provider);
}
