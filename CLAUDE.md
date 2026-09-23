# PrepHub (repositório PrepHub) — Contexto do Projeto

## O que é

> **Marca: PrepHub** (desde 15/09/2026, com a entrada da AWS). Mudou só o nome **exibido** — telas, e-mails, recebedor do Pix. Solução, namespaces, métricas `prephub_*`, volume `prephub_dados` e nomes de cookie continuam `PrepHub`: trocá-los derrubaria sessões, dashboards e o volume de dados sem ganho para quem usa.

Simulado do exame **AZ-900 (Microsoft Azure Fundamentals)** que replica fielmente a experiência real da prova: interface, timer, navegação entre questões, marcação para revisão. O diferencial não é ter "mais um banco de questões" — é a fidelidade à experiência real de prova (estilo Pearson VUE), algo que o simulado oficial da Microsoft não oferece.

Roadmap: outras certificações Microsoft (AZ-104, AZ-305, AZ-400 já publicados) e **AWS**, começando pelo **AIF-C01 (AWS Certified AI Practitioner)**, publicado em 15/09/2026. O modelo de dados deve ser desenhado pensando nisso desde já — nada de hardcoded só pro AZ-900.

## ⚠️ Regra crítica de conteúdo (não negociável)

**Nunca usar, buscar ou reproduzir questões reais vazadas ("dumps") de provas Microsoft.** Isso viola o NDA que se assina ao fazer a certificação e pode levar à revogação do certificado. Todas as questões do banco devem ser **originais**, escritas com base no **Skills Measured outline** público do exame (documento oficial da Microsoft Learn, que lista tópicos e pesos de cada domínio). Mesmo estilo, mesmo formato, mesma dificuldade — conteúdo próprio.

Se em algum momento o Claude Code (ou eu) sugerir "buscar questões que caíram na prova" ou importar de sites de dump, a resposta é não.

**Os practice assessments oficiais do Microsoft Learn caem na mesma regra, por outro motivo.** São públicos e gratuitos, então consultá-los não viola NDA — mas continuam sendo conteúdo proprietário da Microsoft, e adaptá-los para o nosso banco é cópia. O uso legítimo é como **calibre de nível** (profundidade, quanto contexto o cenário carrega, quão próximos ficam os distratores), nunca como fonte. A linha prática: se a questão deles precisa estar aberta ao lado enquanto se escreve a nossa, é cópia; se foi fechada e a nossa saiu do conceito, é calibragem. Com chave Pix numa página pública, conteúdo copiado deixa de ser questão de ética e vira exposição real.

## Stack

- .NET 10, ASP.NET Core MVC
- EF Core + SQLite (`Microsoft.Data.Sqlite`)
- xUnit para testes — **prioridade desde o início**, não deixar pra depois (gap conhecido a corrigir neste projeto)
- UI: priorizar fidelidade visual ao ambiente de prova real (timer fixo no topo, navegação linear + tela de revisão, cores neutras) sobre qualquer frescura visual

## Arquitetura

Clean Architecture, seguindo o padrão já usado em outros projetos (ex: WebAppClinicaMedica):

```
PrepHub.sln
├── src/
│   ├── PrepHub.Domain          # Entidades, Value Objects, regras de negócio puras
│   ├── PrepHub.Application     # Casos de uso, interfaces, DTOs
│   ├── PrepHub.Infrastructure  # EF Core, DbContext, Repositories, SQLite
│   └── PrepHub.Web             # ASP.NET Core MVC (Controllers, Views, ViewModels)
└── tests/
    ├── PrepHub.Domain.Tests
    └── PrepHub.Application.Tests
```

Dependências: `Web` → `Application` + `Infrastructure`; `Infrastructure` → `Application`; `Application` → `Domain`.

## Modelo de domínio (nomes de tipo em português; propriedades em inglês = colunas do banco)

- **Exame** (`Exam`) — Id, Code (ex: "AZ-900"), Name, TimeLimitMinutes, PassingScorePercent, TotalQuestions, Vendor (`FornecedorDoExame`: Microsoft, Aws — decide escala da nota, regras de formato e a tela da prova)
- **AreaDeHabilidade** (`SkillArea`) — Id, ExamId, Name, WeightPercent (ex: "Descrever conceitos de nuvem — 25-30%")
- **Questao** (`Question`) — Id, ExamId, SkillAreaId, Text, Type (`TipoDeQuestao`: EscolhaUnica, EscolhaMultipla, SimNao, Associacao, Ordenacao), Explanation
- **OpcaoDeResposta** (`AnswerOption`) — Id, QuestionId, Text, IsCorrect, OrderIndex, TargetText (nulo fora de `Associacao`)

> **`Associacao` (arrastar e soltar) não trouxe formato de resposta novo — de propósito.** Cada alternativa é um **par candidato** (alvo × item): `TargetText` é o alvo da coluna da direita, `Text` é o item arrastável, e existe uma linha para cada combinação possível. Responder é selecionar um par por alvo, então continua sendo um conjunto de Ids de alternativa: `CorretorDeProva`, `OrdemDasOpcoes`, a gravação da resposta e o histórico seguem sem saber que o tipo existe. A alternativa seria uma tabela de pares com resposta própria, e aí todo caminho do sistema teria de aprender um segundo jeito de estar certo. O custo dessa escolha é o número de linhas (4 alvos × 5 itens = 20 alternativas para uma questão) — irrelevante no volume deste banco. ⚠️ **Sem crédito parcial**: errar um alvo perde a questão inteira, como já vale para múltipla escolha. A prova real dá crédito parcial nos dois formatos; dar em um só é que seria inconsistente.
- **TentativaDeProva** (`ExamAttempt`) — Id, ExamId, StartedAt, FinishedAt, ScorePercent, Passed
- **RespostaDaTentativa** (`ExamAttemptAnswer`) — Id, ExamAttemptId, QuestionId, SelectedOptionIds, IsFlaggedForReview, TimeSpentSeconds

> Nota (nomes entre parênteses): o modelo foi traduzido para português nos **tipos e métodos**; as **propriedades** e o **schema do banco** (nomes de tabela/coluna, fixados por `ToTable`/convenção) seguem em inglês. Correção: `CorretorDeProva` (era `ExamGrader`); placares: `PlacarDaProva`/`PlacarPorArea`. Serviços: `CatalogoDeExamesService`, `SessaoDeProvaService`. Repositórios: `IExameRepository`, `ITentativaDeProvaRepository`.

> **Nota exibida ≠ percentual armazenado.** `PassingScorePercent` continua sendo a regra de negócio (é o que define `Passed`), mas nunca aparece na UI. `EscalaDeNota` converte o percentual para a escala 1–1000 ancorando o percentual de corte do exame em exatamente 700 — assim a nota mostrada e o veredito jamais se contradizem. A escala real da Microsoft é derivada de Teoria de Resposta ao Item e nunca é divulgada; a nossa é uma aproximação linear por partes, deliberadamente documentada como tal.

## Fluxo da prova (o coração do diferencial)

1. Tela inicial → botão "Iniciar Simulado"
2. Tela de questão: timer regressivo fixo, indicador "Item 12 de 40", navegação **linear** (Anterior / Próxima), toggle "Marcar para revisão", botão "Comentários"
3. Tela de revisão (acessível a qualquer momento): tabela de todos os itens com status Completo / Incompleto / Não visto + coluna Marcado, filtros por categoria, clique na linha volta ao item
4. Ao zerar o tempo ou encerrar manualmente → **score report** na escala 1–1000 (corte 700) com desempenho por domínio
5. A revisão questão a questão com gabarito e explicação é uma **tela separada**, acessada a partir do score report — não faz parte da simulação

## Metodologia de criação de questões (o que dá a dificuldade real)

O objetivo é uma questão que **quem só decorou termo erra, e quem entende o conceito acerta** — isso não vem de "questão autêntica vazada", vem de boa engenharia de distrator. Regras pra toda questão nova:

- **Cenário aplicado, não definição.** Nunca "O que é um Resource Group?". Sempre algo como "Uma empresa precisa de X restrição, qual serviço/abordagem atende?" — obriga a aplicar o conceito, não só reconhecer o termo.
- **Distratores plausíveis, não bobos.** Cada alternativa errada deve ser a resposta certa de uma pergunta *ligeiramente diferente* — ex: confundir Availability Zone com Availability Set, Azure Policy com RBAC, Reserved Instances com Spot Instances, o limite entre IaaS/PaaS/SaaS, Cost Management com Advisor. Nada de alternativa absurda que se elimina por eliminação óbvia.
- **Questões negativas ocasionais** ("Qual das opções NÃO é..."), pra quebrar padrão de pattern-matching.
- **Síntese de mais de um tópico do Skills Measured na mesma questão** quando fizer sentido (ex: shared responsibility model + compliance no mesmo cenário) — isso é o que mais separa quem entende de quem decorou frase solta.
- **Mix de formatos**: single choice, multiple response ("selecione duas"), statement-based (afirmação + Verdadeiro/Falso ou Sim/Não) e **arrastar e soltar** (`Associacao`) — são formatos publicamente documentados pela Microsoft como parte do formato de prova, então replicar o formato é ok; o que não pode é replicar o conteúdo real.
- **Recursos nomeados no cenário** (`rg-financeiro-brs`, `stcontosodocs01`, `VM-WEB-01`, assinatura `Contoso-Prod`). Não é enfeite: obriga a raciocinar sobre uma situação concreta em vez de reconhecer um termo solto, que é a diferença de textura entre o banco original e os lotes `-cenarios`.
- **No arrastar e soltar, o alvo é o cenário e o item é o serviço** — nunca o contrário. Com o serviço como alvo, a questão vira definição. E o painel de itens **sempre tem distrator** (item extra, ou item que responde a mais de um alvo): com exatamente um item por alvo, quem sabe todos menos um acerta o último por eliminação, e o validador rejeita.
- **Explicação de cada distrator, não só da resposta certa.** A explicação da questão deve dizer por que cada alternativa errada está errada — é isso que ensina, não o gabarito sozinho.
- Calibrar dificuldade contra o **Skills Measured outline oficial** (pesos por domínio) — não inventar peso de tópico.
- ⚠️ **Dificuldade é calibrada POR EXAME, e o piso acima foi escrito para o AZ-900.** Em AZ-104/305/400 o candidato já opera Azure: questão "aplicada com bom distrator" continua fácil demais, e um simulado que aprova quem seria reprovado é o pior defeito possível num preparatório. O critério é o **teste do "quem responde"** — acerta quem *leu a descrição do serviço* (lixo), quem *entendeu o conceito* (AZ-900), quem *já configurou aquilo* (AZ-104+), ou quem *já escolheu entre duas opções válidas sob restrição real* (AZ-305/400). O salto de AZ-900 para cima é de **reconhecimento para comportamento**: o cenário passa a carregar estado de configuração que determina a resposta, e o distrator deixa de ser o serviço vizinho para ser a **abordagem vizinha** — a que funcionaria noutro cenário, ou funciona e viola uma restrição declarada. Duas armadilhas ao subir o nível: dificuldade não é obscuridade (o alvo é o caminho comum no detalhe que morde, não o canto raro), e cenário longo não é cenário difícil (cada frase tem de eliminar ao menos uma alternativa). Detalhamento em `docs/formato-questoes.md`, seção "Calibragem de dificuldade por exame".

**Duas barreiras que faltavam e agora existem** — as duas cobrem falhas que só apareceriam depois do deploy:

- **`Validar_CatalogoRealEmbutido_EstaIntegro`** roda a validação sobre os arquivos de verdade, contra as áreas de verdade (`PrepHubDbSeeder.AreasPorExame`, público só para isso). Antes, os testes de regra usavam lotes sintéticos e nunca tocavam nos JSONs; a única checagem do catálogo real era o seed derrubando a aplicação no startup — ou seja, o erro aparecia no deploy, não no commit. ⚠️ É **dicionário por código de exame**, não lista plana: área é escopada ao exame, e com todos os slugs num balde só um lote do AZ-104 apontando para `conceitos-de-nuvem` (área do AZ-900) passaria na validação e cairia no domínio errado, com peso de blueprint errado no sorteio. Dois testes irmãos cobrem o resto: `CatalogoRealEmbutido_SoReferenciaExamesDefinidos` (lote com `exameCode` inexistente sumiria do seed **em silêncio**, porque o seed aplica filtrando por código) e `ExamesDefinidos_TemQuestoesSuficientesParaMontarUmaProva` (o sorteio faz `Math.Min(total, pool)`, então exame publicado com banco magro entrega prova curta e sempre parecida, sem nada ligar sintoma a causa).
- **`MigracoesTests`** aplica as migrations num SQLite real e compara o resultado com o modelo atual (o mesmo diff do "pending model changes" do `dotnet ef`). Os testes de persistência usam `EnsureCreated`, que constrói o banco direto do modelo e **pula as migrations** — migration faltando ou fora de sincronia passava por toda a suíte. ⚠️ Vale ainda mais aqui porque `dotnet ef` não roda em máquina sem o runtime do ASP.NET Core instalado, e nesse caso a migration é escrita à mão.

## Especificação da interface de prova (fidelidade é o produto)

Isso não é "nice to have", é o diferencial do PrepHub. A referência de calibração é o **exam sandbox oficial da Microsoft** (`aka.ms/examdemo`) — demo pública da interface de entrega, não é dump de conteúdo. Elementos obrigatórios:

- **Barra superior fixa**: código/nome do exame à esquerda, "Tempo restante" + relógio HH:MM:SS à direita. Fundo claro (cinza), texto escuro — o chrome da prova real é discreto, não uma faixa colorida
- **Indicador de posição**: "Item 12 de 40", no topo da área da questão
- **Opções de resposta**: radio pra single choice, checkbox pra multiple response. Sem "cartão"/borda por alternativa — é radio + texto, com hover e destaque de selecionada
- **Arrastar e soltar**: painel de itens à esquerda, alvos à direita, alvo vazio com borda tracejada. ⚠️ **Arrastar não pode ser o único jeito de responder** — HTML5 drag-and-drop não funciona em tela de toque, e ali a questão ficaria literalmente sem resposta possível. Por isso a interação primária é **selecionar o item e depois o alvo** (que funciona igual no mouse, no toque e no teclado), com o arrastar nativo por cima. Clicar num alvo preenchido sem item selecionado devolve o item ao painel. Nada disso fala com a rede: os checkboxes ocultos que o servidor renderizou continuam sendo a resposta, e é deles que `exam.js` lê a seleção — arrastar é só uma forma de marcar caixa
- **Ordem das alternativas embaralhada por tentativa** (`OrdemDasOpcoes`). Os arquivos de seed escrevem a correta em primeiro — é o que mantém o JSON legível e revisável —, então apresentar na ordem do arquivo ensina a marcar a de cima: o padrão aparece na segunda ou terceira prova e destrói o valor do simulado, porque acertar deixa de exigir entender o conceito. A permutação é **derivada** do par (tentativa, opção), não sorteada na hora: é estável dentro da tentativa (navegar e voltar, recarregar, abrir o gabarito depois mostram a mesma ordem) e diferente a cada prova nova, sem coluna no banco nem estado em memória — e não tem como dessincronizar do que foi respondido, já que a resposta gravada aponta para o Id da opção, não para a posição. ⚠️ **Sim/Não fica de fora**: ali as opções são um par fixo ("Sim" antes de "Não"), não alternativas concorrentes. O `OrderIndex` que sai no DTO é a posição **na tela**; expor o índice do arquivo entregaria o gabarito a quem lesse o HTML
- **Fraseado padronizado**, derivado do modelo e não hardcoded: "Escolha duas." (quantidade vem de `RequiredSelections`), "OBSERVAÇÃO: Cada seleção correta vale um ponto." em múltipla resposta, e a instrução Sim/Não nos itens de afirmação
- **Barra de ações fixa no rodapé**: à esquerda "Marcar para revisão" e "Comentários"; à direita "Tela de revisão", "Anterior", "Próxima". No último item, "Próxima" dá lugar a "Encerrar prova"
- ⚠️ **Sem painel de navegação lateral.** A prova real não tem grid de questões — a navegação é linear e o único jeito de saltar entre itens é pela tela de revisão. Não reintroduzir
- **Tela de revisão**: substitui a área da questão (não é modal). Tabela Item / Status / Marcado, com status **Completo, Incompleto, Não visto** — múltipla resposta parcialmente marcada é Incompleto. Filtros "Revisar todos / incompletos / marcados". Clique na linha volta ao item. Modal de confirmação em "Encerrar prova", com aviso de que não dá pra voltar
- **Score report**: nota na **escala 1–1000 (Microsoft) ou 100–1000 (AWS), corte em 700** (nunca percentual), veredito aprovado/reprovado, régua da escala e barras por domínio **sem números** — igual ao relatório real, que não revela contagem de acertos nem quais itens foram errados
- **Revisão de estudo**: tela à parte (`exam/{id}/review`), com gabarito, explicação por distrator e números por domínio. Deve deixar explícito que não existe na prova real
- **Estilo visual**: neutro e sério — tons de azul/cinza/branco, nada de gamificação, emoji ou cor vibrante. Tem que parecer ambiente de prova, não app de quiz
- **Timer zerado = submissão automática**, sem exceção
- Fluxo deve se comportar como SPA (AJAX/partial views no MVC) — sem recarregar a página inteira a cada navegação de questão, pra não quebrar a imersão

### Tela da AWS (`RealizarAws.cshtml`, `_QuestaoAws.cshtml`)

Não é tema da tela Microsoft: é outra disposição e outro fluxo, e o controller escolhe a view pelo `Vendor` do exame. Calibrada pelo **AWS Exam Demo** público da Pearson VUE (`pearsonvue.com/us/en/redirects/aws/demo-test-enu.html`) — referência de interface, nunca de conteúdo: a questão de exemplo do demo não entra no banco, nem adaptada.

- **Tela de instruções antes da primeira questão** (`InstrucoesAws.cshtml`, rota `exam/start/{examId}`): barra **sem relógio**, faixa só com o esquema de cores, rodapé com "Encerrar exame" à esquerda e "Próxima" à direita. É GET e não cria nada — a tentativa, e com ela o tempo, só começa no POST de "Próxima". O texto das instruções é **nosso** (`_InstrucoesAws.cshtml`); do demo vem só a posição.
- **Barra azul `#006caa`**: nome do exame e do candidato à esquerda; "Tempo restante" (MM:SS, H:MM:SS acima de uma hora) e "Questão N de M" à direita. **Faixa `#4678bd`** logo abaixo: Comentário à esquerda; Marcar para revisão (bandeira) e **Esquema de cores** à direita. Fundo branco, **fonte serifada genérica** (`serif`), como o demo.
- **Esquema de cores**: as nove combinações do demo (preto sobre amarelo-claro, branco sobre preto...), em `_EsquemaDeCoresAws.cshtml` + `aws-esquema.js`. Muda só a área de conteúdo, via `[data-scheme]` e variáveis CSS; a escolha fica no `localStorage` do navegador (conveniência de quem vê, não dado da prova).
- **Rodapé só com Anterior/Próxima**, e "Anterior" **some** (não desabilita) na primeira questão. Não existe "Tela de revisão" até o candidato passar pelo último item: "Próxima" no último item abre a revisão. A partir daí o botão aparece.
- **Revisão** (conferida contra o demo): troca a moldura — faixa com "Instruções" no lugar de Comentário/Marcar, contador de questão oculto, **fundo cinza `#f2f3f5`**. Título "Revisão" e botão "Revisar todas" (teal `#007394`) no alto; abas "Todas (n)" / "Incompletas (n)" com ícone de informação / "Marcadas (n)" (só aparece se houver marcada), que **filtram a tabela**; cartão branco com Questão / Título / Status (selo vermelho "Incompleta") / Marcada (Sim/Não) / link "Revisar". "Revisar todas" **percorre as questões em sequência** (Próxima vai à seguinte e, no fim, volta à revisão). "Encerrar revisão" no canto inferior esquerdo, com modal de confirmação.
- **Instrução no fim do enunciado**, gerada pela tela: "(Selecione DUAS.)", "(Selecione e ordene TRÊS.)". Alternativas com letra (A., B., C., D.).
- **Ordenação e associação por lista suspensa** ("Etapa 1: [Selecione...]"), com os passos listados em marcadores acima. Mesmos checkboxes ocultos da tela Microsoft: a lista suspensa só marca o par.
- **Score report**: escala 100–1000 e **tabela de classificação por seção** ("Atende às competências" / "Precisa melhorar"), no lugar das barras.
- ⚠️ **Inferido, sem print do demo:** a disposição da questão de associação (hoje igual à de ordenação, com lista suspensa por enunciado), o selo "Completa" na revisão (o demo só mostrou questões incompletas), o significado da coluna que o demo chama de "Testing" (tratada como "Marcada"), os rótulos em pt-BR da prova traduzida e o layout do score report.

## Convenções de código

- **Idioma (decisão do projeto):** o domínio é em **português** — nomes de **tipos** (classes/interfaces/enums/records), **métodos**, **arquivos, pastas e namespaces de negócio**. Comentários também em português.
  - **Mantém-se em inglês:** termos de framework/convenção .NET (`Controller`, `DbContext`, `Repository`, `Service`, `Dto`, `Request`, `ViewModel`, `Configuration`, `UnitOfWork`, `Entity`, `Guard`), as pastas de convenção do MVC (`Controllers`, `Views`, `Models`) e de infra (`Persistence`, `Repositories`, `Configurations`), as **propriedades das entidades** e o **schema do banco** (tabelas/colunas) — para não poluir com `HasColumnName` e manter portabilidade.
  - Sufixo técnico + raiz de negócio, ex.: `IExameRepository`, `SessaoDeProvaService`, `QuestaoDto`, `ExameController`, `RealizarProvaViewModel`.
- SOLID; Clean Architecture + DDD leve, mesmo padrão dos outros projetos .NET
- AutoMapper entre Domain e DTOs/ViewModels quando fizer sentido
- Testes unitários (xUnit) desde a primeira etapa — objetivo explícito do projeto, não é opcional

## Autenticação (conta local + login social)

Login **obrigatório** para fazer simulado. Dois caminhos, ambos chegando na mesma entidade `Usuario`: **conta local** (e-mail + senha, com cadastro em `/conta/cadastrar`) e **provedor externo** — Google, LinkedIn, GitHub. Na tela de login o formulário de e-mail/senha vem acima dos botões sociais e o link de cadastro abaixo deles.

- **Sem ASP.NET Core Identity**, inclusive para a conta local. Cookie de autenticação + handlers OAuth + hash de senha próprio, tudo mapeando para a entidade `Usuario` do Domain. Evita as ~7 tabelas do Identity e mantém o schema no padrão do projeto.
- **Senha nunca em texto**: só `PasswordHash` (nulo em conta social) e via `IHasherDeSenha`/`HasherDeSenhaPbkdf2` — PBKDF2-HMAC-SHA256, 600k iterações, salt por senha, comparação em tempo constante. O hash carrega os próprios parâmetros (`pbkdf2-sha256$iteracoes$salt$hash`), então subir o fator de trabalho (ou trocar o algoritmo) não invalida senha já cadastrada. `PoliticaDeSenha` no Domain é a única fonte do mínimo/máximo, lida por Application e pelas anotações da ViewModel.
- **Falha de login nunca distingue** e-mail inexistente, senha errada, conta bloqueada e conta que só existe num provedor social — todas devolvem `CredenciaisInvalidas` e a mesma mensagem, e os caminhos que não chegam a conferir o hash real ainda verificam um hash de referência para não vazar a resposta pelo tempo. Cadastro é a exceção: aí a duplicata é inevitável, então a mensagem diz o que fazer.
- **Conta local só entra depois de confirmar o e-mail** (`EmailConfirmedAt` em `Usuario`, `TokenDeConfirmacaoDeEmail`, validade de 24h em `PoliticaDeConfirmacaoDeEmail`). Cadastrar **não autentica mais**: cria a conta, emite o link e leva para `/conta/confirme-seu-email`. Sem isso, um endereço digitado errado vira conta que ninguém alcança — e o dono só descobre quando pede "esqueci minha senha" e o link não chega a lugar nenhum, momento em que não há mais nada a fazer. O banco tinha exatamente esse caso (`…@gmai.com`, sem o "l").
- ⚠️ **`EmailNaoConfirmado` é a única falha de login que a tela nomeia, e a ORDEM é o que a torna segura**: a confirmação é conferida **depois** do hash da senha. Quem lê a mensagem já provou saber a senha, então não descobriu nada. Mover essa checagem para antes da verificação transformaria o login no oráculo de cadastro que `CredenciaisInvalidas` existe para impedir — e nada quebraria para avisar, por isso há teste dedicado à ordem.
- **Conta social nasce confirmada** (`EmailConfirmedAt = createdAt` no construtor): o provedor já verificou o endereço, e o GitHub pode não devolver e-mail nenhum — exigir confirmação ali travaria o login por um dado que nunca vai chegar. **Redefinir a senha também confirma** (`DefinirNovaSenha`): receber o link naquela caixa de entrada é a mesma prova que a confirmação pede.
- **Reenvio em `/conta/reenviar-confirmacao`**, com a mesma disciplina de `/conta/esqueci-senha`: resposta idêntica exista conta pendente, já confirmada, social ou nenhuma. Um link novo invalida o anterior. Confirmar duas vezes é **sucesso**, não erro — clique duplo e varredor de links do provedor de destino abrem a mesma URL, e mandar para uma tela de erro quem já pode entrar seria defeito. É também por isso que confirmar **não autentica**: o `GET` pode não ter sido humano, então ninguém ganha sessão por ele.
- ⚠️ **A migration `ConfirmacaoDeEmail` traz um `UPDATE Users SET EmailConfirmedAt = CreatedAt`, e ele não é opcional.** A coluna nasce nula e nulo significa "não entra": sem o backfill, publicar a migration trancaria fora toda conta já existente — inclusive as sociais, que não têm por onde confirmar — sem nenhum link emitido para recuperá-las.
- **Duas camadas contra força bruta, de propósito.** (1) `LimiteDeTentativasSetup`: 10 requisições por IP a cada 5 min nos POSTs de login/cadastro/esqueci-senha/redefinir-senha, via rate limiter nativo; `OnRejected` manda `Retry-After` e redireciona para `/conta/login?limite=1` (redirect e não corpo 429 porque são formulários de navegador). ⚠️ O contador é **em memória, por instância** — mesmo gatilho do PostgreSQL: em multi-instância a cota se multiplica pelas réplicas. (2) `PoliticaDeTentativasDeLogin`: 5 falhas consecutivas bloqueiam a conta por 15 min. A primeira camada não segura ataque distribuído contra uma conta conhecida; a segunda não segura quem varre muitas contas. O aviso visível ("muitas tentativas") é só o da camada de IP, que é por máquina e não revela cadastro; o bloqueio por conta é **silencioso**, senão viraria confirmação de que aquele e-mail tem conta.
- **Bloqueio expira sozinho e nunca é permanente** — bloqueio longo viraria arma para trancar a conta de outra pessoa só errando a senha de propósito. Acerto zera o contador (o limite é de falhas *consecutivas*) e redefinir a senha libera o bloqueio, porque quem provou controlar o e-mail é o dono.
- **Redefinição de senha por link de uso único** (`/conta/esqueci-senha` → `/conta/redefinir-senha?token=…`): token de 256 bits em Base64Url, guardado como **hash SHA-256** (`IGeradorDeTokenSeguro`), validade de 1h (`PoliticaDeRedefinicaoDeSenha`), invalidado ao ser usado, ao pedir um link novo e ao concluir a troca. SHA-256 puro aqui é o certo, ao contrário da senha: token aleatório não tem dicionário a resistir, e o hash **precisa** ser determinístico para servir de chave de busca. A tela responde igual exista ou não conta com aquele e-mail, e o `GET` valida o link antes de mostrar o formulário. Não autentica automaticamente depois da troca (ao contrário do cadastro): o cookie não tem selo de segurança, então sessões antigas em outros dispositivos **continuam válidas** — dívida conhecida.
- **E-mail via `IEnviadorDeEmail`**, registrado conforme a seção `Email` do config: com `Email:SmtpHost` sai por SMTP (`EnviadorDeEmailSmtp`); sem ele, `EnviadorDeEmailParaLog` grava a mensagem inteira no log em nível Warning — é assim que se testa "esqueci minha senha" e a confirmação de cadastro em desenvolvimento, copiando o link do console. Falha de envio é registrada e engolida, porque propagá-la mudaria a resposta só no caminho em que o e-mail existe.
- ⚠️ **O `catch` do `EnviadorDeEmailSmtp` é amplo de propósito.** A lista de tipos que havia antes (`SmtpException`, `InvalidOperationException`, `IOException`) deixava escapar justamente o que acontece contra servidor real: `AuthenticationException` no handshake TLS, `SocketException` com host errado, timeout do `SmtpClient`. Qualquer uma subiria ao controller e estouraria a página **só** no caminho em que a conta existe — e aí "esqueci minha senha", que responde igual para todo mundo para não dizer quem tem cadastro, passaria a dizer. Cancelamento pedido por quem chamou é rethrown, e só ele.
- **"O e-mail não está chegando" quase nunca é o SMTP.** Antes de mexer em credencial, olhe o log: `Pedido de redefinição para e-mail sem conta local` significa que o `EnviarAsync` **nem foi chamado** — o endereço não tem conta local (foi digitado errado no cadastro, ou a conta é social e não tem senha nossa). Só `Falha ao enviar e-mail por SMTP` aponta para o servidor. E remetente `@gmail.com` para Outlook/Hotmail cai em lixo eletrônico com frequência: entrega é o terceiro suspeito, não o primeiro.
- **Dois esquemas de cookie** (`EsquemasDeAutenticacao`): `Aplicacao` é a sessão; `Externo` é temporário e só existe entre o callback do provedor e a criação da sessão. É esse intervalo que permite trocar as claims externas por um `Usuario` local.
- **Chave natural da identidade é o par (`Provider`, `ProviderKey`)**, nunca o e-mail — o GitHub pode não devolver e-mail, e a mesma pessoa pode ter o mesmo e-mail em dois provedores. Na conta local o `ProviderKey` é o **e-mail normalizado** (`Usuario.NormalizarEmail`: trim + minúsculas), então "uma conta local por e-mail" cai no mesmo índice único, sem regra nova — e um e-mail que também aparece numa conta Google segue sendo outro usuário, exatamente como já valia entre Google e GitHub. Vincular contas (ou somar senha a uma conta social) é recurso à parte, ainda não implementado.
- **`NameIdentifier` no cookie da aplicação é o Id LOCAL do `Usuario`**, não o id do provedor. `IUsuarioAtual` lê exatamente essa claim.
- **Posse da tentativa é imposta na Application**, não no controller: `SessaoDeProvaService` resolve toda tentativa por `ObterTentativaDoUsuarioAsync`, que devolve `null` para tentativa de outro dono (o Web responde 404 e não confirma que o id existe). O controller não tem como esquecer de checar porque não é ele quem checa.
- **Política padrão exige autenticação** (`SetFallbackPolicy`); o que é público leva `[AllowAnonymous]` explícito — hoje login/callback, cadastro e a página de erro.
- **`/conta/entrar` é POST com antiforgery**, não GET: um GET permitiria login CSRF, prendendo a pessoa numa conta que não é dela. Vale igual para `/conta/entrar-com-senha` e `/conta/cadastrar`. `returnUrl` passa por `Url.IsLocalUrl` para barrar open redirect.
- **Validação de senha é server-side**, sem jQuery validation: a tela reexibe com as mensagens do `ModelState`. As telas de login/cadastro não carregam script nenhum, no mesmo espírito do resto do projeto.
- **Credenciais nunca no appsettings versionado** — usar `dotnet user-secrets` em `Authentication:{Google,GitHub,LinkedIn}:{ClientId,ClientSecret}`. Cada provedor só é registrado se tiver credencial, então a app sobe e a tela de login funciona com apenas um deles configurado — ou com nenhum, já que a conta local não depende de credencial externa (o bloco "ou continue com" simplesmente não aparece).
- **LinkedIn usa OpenID Connect** ("Sign In with LinkedIn using OpenID Connect", escopos `openid`/`profile`/`email`, userinfo em `/v2/userinfo`) — é preciso habilitar esse produto no app do LinkedIn, senão o callback volta com `unauthorized_scope`. **GitHub exige o escopo `user:email`**, sem ele não vem e-mail nenhum.

## Sustentação: doação (não é venda)

O projeto é **gratuito e sem anúncios**, e a sustentação atual é **doação voluntária via Pix**, na página `/apoiar`. A decisão consciente foi começar por doação em vez de produto pago: não exige CNPJ, gateway, webhook nem mudança no banco, então sobe junto com o deploy sem virar projeto paralelo.

- **Doar não desbloqueia nada** — e isso é regra, não retórica. No instante em que existir contrapartida, deixa de ser doação e vira venda, com as consequências fiscais e legais que se quis evitar agora. Todo simulado, a tela de revisão e o gabarito comentado continuam abertos a qualquer pessoa.
- **Chave Pix não é segredo**, ao contrário de todo o resto do config: ela é publicada de propósito. Por isso mora no `appsettings.json` versionado, e **não** em `dotnet user-secrets` — guardá-la como segredo daria falsa sensação de proteção a um dado exibido na tela. Usar sempre **chave aleatória**: CPF, telefone ou e-mail ficariam expostos numa página pública, e chave aleatória se troca sem mexer em mais nada.
- **Sem `Doacao:ChavePix`, a funcionalidade inteira desaparece**: `/apoiar` responde 404 e nenhum link para ela é renderizado. Mesma disciplina de `OpcoesDeEmail.EstaConfigurado` e dos provedores OAuth — não existe estado "página de doação meio pronta" pedindo dinheiro para lugar nenhum.
- **Onde o assunto aparece, e onde não aparece.** Rodapé permanente (discreto) e uma linha no fim do score report, **depois** dos botões — ponto do fluxo em que o valor já foi entregue. ⚠️ **Nunca durante a prova**: a barra superior, a questão e a barra de ações são intocáveis, porque a imersão é o produto. Nada de modal de entrada, banner fixo ou barra de meta de arrecadação — barra de progresso de doação é gamificação, que o projeto proíbe em qualquer forma.
- **`BrCodePix` monta o payload EMV do Pix** (`Infrastructure/Doacao/`): campos `ID+tamanho+valor` em ordem crescente, fechados por CRC-16/CCITT-FALSE. É código sem I/O e determinístico, então é testado por igualdade de string contra o **exemplo do manual do BR Code do Banco Central** — um caractere fora do lugar faz o aplicativo do banco recusar o código inteiro sem dizer onde está o erro, e não há como descobrir isso em produção sem transferir dinheiro de verdade.
- O valor é sempre formatado com **ponto decimal e cultura invariante**. Numa máquina em pt-BR a formatação padrão escreveria `15,00` e o código quebraria só em produção.
- **QR gerado no servidor** (`QRCoder`, SVG), nunca por API pública de QR: as alternativas "fáceis" mandariam a chave Pix para um terceiro a cada visita e deixariam uma página nossa refém da disponibilidade dele.
- **Só valor da lista `ValoresSugeridos` entra no payload.** Qualquer outra coisa vinda da query string vira valor aberto — sem isso, uma URL compartilhada poderia gerar QR de valor arbitrário em nome do projeto.
- **Marca:** dizer "simulado preparatório para o exame AZ-900" é uso nominativo legítimo; usar logo da Microsoft ou sugerir endosso oficial, não. O risco sai do zero quando existe dinheiro envolvido, e a regra de questões originais deixa de ser só ética para virar blindagem.

## Banco de dados

SQLite via EF Core Migrations. Arquivo em `src/PrepHub.Web/App_Data/prephub.db` (ajustável). Evitar recursos específicos de um único provider na modelagem — se o projeto crescer, a migração pra PostgreSQL deve ser barata.

**Decisão (mantida com login social):** seguir no SQLite, com **WAL habilitado** no startup (leitores param de bloquear o escritor). O que quebraria o SQLite não é volume nem autenticação, é **deploy multi-instância ou disco efêmero** — esse é o gatilho para migrar pro PostgreSQL, não uma data. É a disciplina provider-agnostic acima que mantém essa migração barata.

> ⚠️ Dívida conhecida: `ScorePercent` é `decimal` e o SQLite não tem tipo decimal nativo, então ordenação/comparação numérica é imprecisa. Hoje não morde porque ninguém ordena por nota — vai morder quando existir histórico ordenado ou ranking.

## Docker

`Dockerfile` (multi-stage: SDK 10 compila, `aspnet:10.0` roda) + `docker-compose.yml`. A imagem roda como usuário sem privilégio (`USER $APP_UID`), escuta na **8080** (e na **9464**, só métricas — ver Observabilidade) e se auto-inicializa: as migrations e o seed rodam no startup, então subir com volume vazio já cria o banco.

O compose sobe **três** serviços: a aplicação, o Prometheus e o Grafana. (Numa máquina apertada os dois últimos podem ficar sob demanda — ver "Rodar em máquina pequena" no fim desta seção.)

```bash
cp .env.exemplo .env          # credenciais de OAuth/SMTP; funciona vazio
docker compose up --build
# http://localhost:8080       aplicação
# http://localhost:3000       Grafana (admin/admin por padrão)
# http://localhost:9090       Prometheus
```

- **Backup:** `tools/backup-dados.sh` empacota o volume `prephub_dados` (banco + chaves de Data Protection). Ele **para o container** por alguns segundos de propósito: o SQLite roda em WAL, então parte das escritas confirmadas vive no arquivo `-wal` até o checkpoint, e copiar com a aplicação escrevendo produz uma cópia que às vezes abre sem as últimas transações — defeito que só aparece no dia da restauração. Restaurar é `docker compose down`, recriar o volume a partir do `.tar.gz` e subir; ver `docs/deploy.md`.
- **`App_Data` é o único estado que precisa sobreviver** e é onde o volume nomeado `dados` monta: banco SQLite **e** chaves de Data Protection. Container tem disco efêmero — que é exatamente o gatilho de migração para PostgreSQL citado acima; o volume neutraliza o gatilho enquanto for **uma instância só**. Escalar réplicas continua sendo o momento de trocar de banco (e o limitador de tentativas, que é em memória, tem o mesmo limite).
- **As chaves de Data Protection são persistidas de propósito** (`PersistKeysToFileSystem` em `Program.cs`). Sem isso, cada restart geraria chaves novas: todos os cookies de sessão invalidados e os formulários quebrando com "the antiforgery token could not be decrypted". É o erro mais comum ao containerizar ASP.NET Core e não aparece em teste rápido, só depois do primeiro deploy.
- O aviso `No XML encryptor configured` no boot é esperado: em Linux não há DPAPI, então as chaves ficam em claro **dentro do volume**. Quem protege é o acesso ao volume; cifrá-las exigiria certificado, o que só faz sentido com um deploy real definido.
- **Volume nomeado, não bind mount.** O volume herda o dono do diretório na imagem (o usuário sem privilégio); uma pasta do host entraria com o UID do host e o processo não conseguiria gravar.
- **`dotnet user-secrets` não existe no container** — é ferramenta de desenvolvimento. No container tudo vem de variável de ambiente, com `__` no lugar de `:` (`Authentication__Google__ClientId`). O `.env` é gitignored; o modelo versionado é `.env.exemplo`.
- **Callback de OAuth tem de casar com a porta publicada** (`http://localhost:8080/signin-google` etc.). Trocar host/porta exige recadastrar no painel de cada provedor.
- `/health` responde `200 ok` sem tocar no banco e sem exigir login. Não há `HEALTHCHECK` no Dockerfile porque a imagem de runtime não traz curl/wget — quem orquestrar aponta para o endpoint.
- O aviso `Failed to determine the https port for redirect` também é esperado: o container serve HTTP e o TLS termina no proxy à frente. **Atrás de proxy reverso, ligue a seção `ProxyReverso`** (`ProxyReversoSetup`, em `Web/Rede/`): sem ela o ASP.NET monta os `redirect_uri` do OAuth com `http://`, os provedores recusam, e o esquema errado ainda faz `UseHttpsRedirection` entrar em laço com o proxy. Fica **desligada por padrão** porque só é correta quando existe proxy de fato — ligada sem proxy, a aplicação passa a acreditar em cabeçalhos que o próprio cliente escreve. ⚠️ É preciso declarar de onde os cabeçalhos podem vir (`RedesConhecidas`/`ProxiesConhecidos`): habilitada sem nenhuma origem, o middleware **descarta tudo em silêncio** e o sintoma é login social recusado pelo provedor, longe daqui — por isso esse estado é registrado como erro no boot. O modo `ConfiarEmQualquerProxy` existe como último recurso e é inseguro: o IP do cliente passa a ser escolhido por quem envia a requisição, e é ele a chave do limitador de tentativas. `UseProxyReverso()` é o **primeiro** middleware do pipeline, e a ordem é o que o faz funcionar. Passo a passo em `docs/deploy.md`.
- A imagem **não roda os testes** no build. `dotnet test` fica no fluxo local/CI, para o build da imagem não pagar esse tempo a cada deploy.

### Rodar em máquina pequena (VM de ~1 GB)

O ajuste de máquina apertada mora em **`docker-compose.override.yml`**, que é **gitignored**: ele descreve uma máquina, não o projeto. Versionado, aplicaria teto de memória em qualquer clone — inclusive num servidor grande, onde só atrapalharia. O `docker-compose.yml` continua sendo o padrão neutro.

- ⚠️ **O nome do arquivo é a única coisa que faz o mecanismo funcionar.** `docker compose` soma `docker-compose.override.yml` automaticamente, sem `-f`. Qualquer variação (`docker-compose-override.yml`) é **ignorada em silêncio**: a stack sobe sem nenhum ajuste e nada avisa.
- **O build é a etapa mais pesada, e nenhum limite do compose o alcança.** `mem_limit` vale para o container em execução; a etapa de build não tem knob de memória, nem no compose nem no BuildKit. Por isso o Dockerfile expõe **`ARG MSBUILD_ARGS`** (vazio por padrão, para não penalizar CI nem máquina com RAM sobrando) — passar `-m:1 -p:UseSharedCompilation=false` serializa os projetos e mantém o Roslyn dentro do MSBuild em vez de um servidor à parte, derrubando o pico de ~1 GB para ~400 MB. É o único ponto de controle que existe.
- **`MSBUILDDISABLENODEREUSE=1` no estágio de build** é ganho puro em container: o MSBuild deixa processos vivos para o build seguinte reaproveitar, e num estágio descartado esse "seguinte" nunca chega — o nó só fica residente ocupando memória.
- **Limite de memória não acelera nada; ele contém.** Sem limite, quando a RAM acaba quem escolhe a vítima é o OOM killer do kernel, por score — e o processo mais gordo da máquina pode ser o `sshd` ou o editor. Perde-se a sessão em vez do serviço. Com limite, o estouro é do container e o `restart: unless-stopped` o traz de volta.
- ⚠️ **Não igualar `memswap_limit` a `mem_limit`** — isso desliga o swap do container, e é o reflexo errado aqui. O swap é o amortecedor que transforma pico em lentidão passageira em vez de processo morto.
- **Server GC é o padrão do SDK Web e é a premissa errada em VM pequena**: ele aloca um heap por núcleo e coleta preguiçosamente, porque assume servidor dedicado. `DOTNET_gcServer=0` + `DOTNET_GCConserveMemory=9` troca vazão (irrelevante com um usuário) por RSS, tipicamente pela metade. Com `mem_limit` presente não é preciso configurar teto de heap: o .NET **lê o cgroup** e define ~75% dele sozinho.
- **Observabilidade sob demanda via `profiles`** é o maior ganho, e não é limite de memória: Prometheus e Grafana juntos custam mais RAM que a aplicação, para algo que se faz de vez em quando — olhar um gráfico. Como a coleta é **pull**, a aplicação não percebe a ausência deles. O preço é buraco no histórico enquanto o perfil fica desligado; se o histórico contínuo passar a importar, o lugar dele não é uma VM de 1 GB.
- ⚠️ **`command` é lista, e lista de override SUBSTITUI a original — não é somada.** Ao acrescentar flag ao Prometheus, os argumentos originais têm de ser repetidos; esquecer o `--config.file` faz o serviço subir sem coletar nada, e o sintoma (Grafana vazio) aponta para o lugar errado.
- **Retenção do Prometheus é disco, não RAM.** Ele mantém em memória o bloco corrente (~2h), não os 90 dias; quem determina a memória é a **quantidade de séries**. Reduzir retenção "para economizar RAM" é otimização de fachada. O teto de memória que importa é **`--query.max-samples`**: o padrão de 50M amostras deixa uma consulta larga alocar vários GB e derrubar a máquina antes de qualquer limite reagir.
- **`GOMEMLIMIT` abaixo do `mem_limit`** nos dois serviços em Go (Prometheus e Grafana): é o runtime se ajustando — ao aproximar-se do teto passa a coletar mais agressivamente — em vez do kernel matando o processo. Ficar lento em vez de morrer.
- ⚠️ **`docker compose config` imprime o `.env` em texto puro**, credenciais de OAuth e senha de SMTP inclusive. É o comando natural para conferir um merge de override, e a saída é fácil de colar num issue sem perceber.

## Observabilidade (Prometheus + Grafana)

Responde "o que está acontecendo no projeto": quantas contas existem, quanta gente ainda volta, quantas provas rodam e como o processo está de saúde. Tudo versionado em `observabilidade/` — datasource e dashboards são **arquivos**, não desenhos guardados dentro do Grafana, então um container recriado do zero sobe já configurado.

**A cadeia inteira, do evento ao painel:**

1. O caso de uso chama `IMetricasDeNegocio` (Application) — nunca o controller. Mesma disciplina da posse da tentativa: o controller não tem como esquecer de contar o que não é ele quem conta.
2. `MetricasDoPrepHub` (Infrastructure) implementa a porta com um `Meter` da **BCL** (`System.Diagnostics.Metrics`). Nenhum pacote de vendor nessa camada.
3. `ObservabilidadeSetup` (Web) liga o OpenTelemetry ao meter e publica `/metrics` no formato do Prometheus.
4. O Prometheus busca (`scrape`) esse endpoint a cada 15s; o Grafana só desenha o que pergunta a ele. **Painel vazio quase sempre é problema do Prometheus, não do Grafana.**

**Estoque x movimento — a distinção que organiza tudo.** Evento (login, prova encerrada) vira **contador**, e se pergunta por taxa: `rate()`, `increase()`. Contador zera quando o processo reinicia, então nunca serve de total histórico. Estoque (contas cadastradas, provas realizadas) vira **medidor**, recontado no banco pelo `ColetorDeMetricasDoBanco` a cada 30s — é isso que sobrevive a todo deploy.

- **O medidor não lê o banco na hora do scrape.** A leitura de um instrumento observável é síncrona e roda dentro da coleta: consultar o SQLite ali deixaria o Prometheus preso em I/O e, com o banco travado, faria a coleta inteira expirar. O preço de recontar em segundo plano é o painel enxergar o banco com até um intervalo de atraso — irrelevante para "quantas contas existem".
- **`prephub_coleta_idade_seconds` existe para denunciar coletor morto.** Se ele parasse em silêncio, todos os medidores congelariam mostrando o último valor conhecido com cara de valor atual. Uma exceção no coletor é registrada e engolida de propósito: a partir do .NET 6 ela derrubaria o processo, e derrubar o site porque a *contagem* de usuários falhou seria trocar um painel defasado por uma aplicação fora do ar.
- **Antes da primeira coleta os medidores não publicam nada**, em vez de publicar zero. Lacuna no gráfico é "ainda não sei"; zero seria "não há nenhuma conta cadastrada" dito com toda a confiança.

**`prephub_confirmacoes_email_total{etapa}` é o termômetro da entrega.** A distância entre `link_emitido` e `concluida` é a única medida que existe de quantas contas nascem inalcançáveis — endereço digitado errado ou mensagem barrada pelo filtro de spam do destino. Sem esses dois pontos, uma quebra no envio apareceria só como "menos gente usando", que não aponta para lugar nenhum. `reenvio_solicitado` cumpre para a confirmação o mesmo papel que `solicitada` cumpre na redefinição. E `ResultadoDeLogin.EmailNaoConfirmado` tem rótulo próprio de propósito: junto com `senha_incorreta`, uma quebra no envio de e-mail — dezenas de pessoas presas na porta — apareceria no painel com a cara de força bruta.

**As métricas de login distinguem o que a tela não distingue** — e é esse o ponto. `prephub_logins_total{resultado}` separa `conta_inexistente`, `senha_incorreta`, `conta_bloqueada` e `pedido_invalido`, enquanto a tela devolve a mesma mensagem para todos, porque diferenciar ali viraria oráculo para descobrir quem tem conta. O painel é interno, o rótulo é um enum fechado sem e-mail nem id, e contar custa microssegundos contra os ~200ms do PBKDF2 — não abre canal de tempo. Sem essa separação, força bruta apareceria como uma linha genérica de "falhas". Mesma lógica em `prephub_redefinicoes_senha_total`: a distância entre `solicitada` e `link_emitido` é alguém varrendo e-mails à procura de quem tem cadastro.

- ⚠️ **Nada que identifique uma pessoa pode virar rótulo.** E-mail, nome ou id de usuário criariam uma série temporal por pessoa: estoura a memória do Prometheus (alta cardinalidade) e transforma o painel num cadastro exposto. Por isso a porta só aceita enums e código de exame. Vale igual para as métricas de HTTP, que rotulam pelo **template** da rota (`exam/{attemptId}/answer`) e não pela URL concreta.

**`/metrics` fica numa porta separada (9464), que o compose não publica no host.** O endpoint é anônimo por obrigação — o Prometheus não sabe fazer login — e o que ele devolve não é inócuo. A defesa é de rede: a porta existe só dentro da rede do Docker. Um token seria pior, porque o arquivo de configuração do Prometheus **não interpola variável de ambiente** e o segredo acabaria versionado em texto para não quebrar o `docker compose up`.

- Na porta pública, `/metrics` não casa com endpoint nenhum e cai na política padrão de autenticação: responde um redirect para o login, **byte a byte igual ao de qualquer caminho inexistente**. Nem os números vazam, nem se confirma que o endpoint existe em algum lugar.
- A porta precisa estar em `ASPNETCORE_HTTP_PORTS` (`8080;9464` no Dockerfile) **e** em `Observabilidade:PortaDeMetricas`. Se discordarem, a aplicação sobe normalmente e **avisa no log** — sem esse aviso a falha seria muda: `/metrics` sem responder em lugar nenhum, alvo "down" no Prometheus e Grafana vazio, sem pista de onde olhar.
- Em desenvolvimento `PortaDeMetricas` é **0**: serve na porta da aplicação, porque `dotnet run` abre só as portas do launchSettings e não há rede interna para isolar. `http://localhost:5090/metrics` mostra a saída crua.
- ⚠️ **Quem colocar um proxy reverso na frente tem de manter a 9464 fora dele** — é o mesmo cuidado registrado na seção `ProxyReverso` do Docker.
- Grafana e Prometheus são publicados **só em `127.0.0.1`**. Sem esse prefixo o Docker escreveria regras de iptables abrindo as portas em todas as interfaces, furando o firewall do host — e nenhum dos dois tem autenticação que sirva para internet aberta.

**⚠️ `metric_name_validation_scheme: legacy` no `prometheus.yml` não é detalhe.** O OpenTelemetry nomeia instrumentos com ponto (`prephub.usuarios.cadastrados`) e o Prometheus 3 passou a aceitar UTF-8: sem essa configuração ele guarda o nome **com ponto**, e `sum(prephub_usuarios_cadastrados)` não encontra nada — a série existe com outro nome, e a consulta exigiria a sintaxe entre aspas (`sum({"prephub.usuarios.cadastrados"})`), que praticamente nenhum exemplo ou dashboard da internet usa. Voltando ao esquema clássico, o exportador entrega tudo já traduzido para underscore.

**Métrica é testada porque falha calada.** Um contador que deixa de ser chamado não quebra nada, não lança nada e não aparece em log nenhum — só produz um painel plano, idêntico a um dia sem movimento. Nome de instrumento e valor de rótulo são contrato com os dashboards que o compilador não vê, então os testes os afirmam como **literais** (`"conta_inexistente"`, e não derivado do enum). O caso que mais justifica a suíte é o encerramento por tempo esgotado: acontece num caminho separado do "Encerrar prova", e instrumentar só o clique perderia justamente as provas de quem não terminou a tempo — sobrando um número que *parece* certo, com a taxa de aprovação inflada e nenhum sinal de que faltava metade dos dados.

- Marca é palavra só: `LinkedIn` vira `linkedin`, não `linked_in`. A regra geral de rótulo separa por maiúscula do meio (para `SenhaIncorreta` virar `senha_incorreta`) e precisa da exceção.
- O `Meter` declara `scope: this`. Em produção não muda nada (a assinatura e o Prometheus enxergam só o nome); existe porque o `MetricCollector` filtra por escopo, e sem ele duas instâncias em paralelo — xUnit roda classes de teste concorrentemente — alimentariam o mesmo coletor, com falha intermitente e sem explicação.
- `LeitorDoRetratoDoBanco` é testado contra **SQLite real**: o risco ali é de tradução, e um `GroupBy` que o provider não converte compila, sobe e só estoura em tempo de execução — dentro do serviço em segundo plano, cuja exceção é engolida. A falha apareceria como painel vazio, não como teste vermelho.

**O que já vem de graça, sem instrumentar nada.** Como os instrumentos são `System.Diagnostics.Metrics`, o ASP.NET Core e o runtime .NET entram na mesma coleta: `http_server_request_duration_seconds` (latência, status e rota), `aspnetcore_rate_limiting_requests_total` (o limitador de tentativas por IP, com `acquired` x `rejected`), `kestrel_*` e `dotnet_gc_*` / `dotnet_process_*`. É metade do dashboard "Aplicação". O scrape do próprio `/metrics` é excluído com `DisableHttpMetrics()` — senão, num projeto com pouco movimento, a coleta seria a rota mais acessada do painel.

**Custo do log.** Recontar o banco a cada 30s gera seis consultas por volta, e o EF Core registra cada comando SQL em nível Information. Daí `Microsoft.EntityFrameworkCore.Database.Command: Warning` no `appsettings.json` — sem isso o log vira uma esteira infinita de `SELECT COUNT(*)` e o que importa afunda no meio. O `appsettings.Development.json` devolve o nível Information, porque em desenvolvimento ver o SQL é justamente o que se quer.

## Comandos úteis

```bash
dotnet build
dotnet run --project src/PrepHub.Web
dotnet ef migrations add NomeDaMigration --project src/PrepHub.Infrastructure --startup-project src/PrepHub.Web
dotnet ef database update --project src/PrepHub.Infrastructure --startup-project src/PrepHub.Web
dotnet test

# Observabilidade — ver o que a aplicação está publicando, sem passar por Prometheus nem Grafana.
# Em desenvolvimento (PortaDeMetricas = 0) o endpoint responde na porta da própria aplicação:
curl -s http://localhost:5090/metrics | grep '^prephub'

# No container a porta é outra e não é publicada no host — só a rede do compose a alcança:
docker compose exec prometheus wget -qO- http://web:9464/metrics | grep '^prephub'

# Os alvos que o Prometheus está coletando (health "up"/"down" e o último erro de cada um):
curl -s 'http://localhost:9090/api/v1/targets?state=active' | python3 -m json.tool

# Rodar uma consulta PromQL direto, para saber se o painel está vazio por falta de dado:
curl -s --get http://localhost:9090/api/v1/query --data-urlencode 'query=sum(prephub_usuarios_cadastrados)'

# Credenciais OAuth (nunca commitar — ficam fora do repositório)
cd src/PrepHub.Web
dotnet user-secrets init
dotnet user-secrets set "Authentication:Google:ClientId"     "..."
dotnet user-secrets set "Authentication:Google:ClientSecret" "..."
dotnet user-secrets set "Authentication:GitHub:ClientId"     "..."
dotnet user-secrets set "Authentication:GitHub:ClientSecret" "..."
dotnet user-secrets set "Authentication:LinkedIn:ClientId"     "..."
dotnet user-secrets set "Authentication:LinkedIn:ClientSecret" "..."

# SMTP para o e-mail de redefinição de senha (opcional) — ver a seção "Envio de e-mail".
dotnet user-secrets set "Email:SmtpHost"           "smtp.gmail.com"
dotnet user-secrets set "Email:SmtpPort"           "587"
dotnet user-secrets set "Email:Usuario"            "seu-endereco@gmail.com"
dotnet user-secrets set "Email:Senha"              "senha-de-app-de-16-caracteres"
dotnet user-secrets set "Email:RemetenteEndereco"  "seu-endereco@gmail.com"
```

### Envio de e-mail: três modos, do mais simples ao de produção

**1. Nada configurado (padrão) — link no log.** Sem `Email:SmtpHost` a app usa
`EnviadorDeEmailParaLog`: a mensagem inteira aparece no console em nível Warning e você copia o
link do terminal. É o suficiente para desenvolver e não exige conta em lugar nenhum.

> Regra de decisão (`OpcoesDeEmail.EstaConfigurado`): usa SMTP quando há host **e** as
> credenciais não estão pela metade. Credencial nenhuma com host preenchido é válida (é o
> servidor local de teste, que não autentica); **usuário sem senha não é** — cai no log e a
> mensagem diz qual chave falta. Sem essa regra, apontar para o Gmail antes de colar a senha de
> app faria o envio falhar em silêncio e o link desaparecer das duas pontas.

**2. Servidor SMTP local de teste — vê o envio real, sem conta.** `tools/smtp-de-teste.py` é um
servidor SMTP mínimo (só stdlib) que imprime o e-mail decodificado e destaca o link:

```bash
python3 tools/smtp-de-teste.py           # terminal 1

# terminal 2 — vale só nesta execução, não grava nada:
Email__SmtpHost=127.0.0.1 Email__SmtpPort=1025 Email__UsarSsl=false \
    dotnet run --project src/PrepHub.Web
```

Serve para exercitar `EnviadorDeEmailSmtp` de verdade (conexão, `MAIL FROM`, `DATA`) antes de
apontar para um provedor. Só escuta em `127.0.0.1` e não valida nada — desenvolvimento apenas.

**3. Gmail com senha de app — e-mail que chega mesmo, sem domínio próprio.** O Gmail não exige
domínio: o remetente é o próprio endereço da conta. Passos:

1. Ativar **verificação em duas etapas** na conta Google (sem isso o Google não oferece senha de
   app, e o acesso por senha comum foi descontinuado — não há como contornar).
2. Gerar uma **senha de app** em `myaccount.google.com/apppasswords` (16 caracteres, sem espaços).
3. Rodar os `dotnet user-secrets set` do bloco acima, com `Email:Usuario` **e**
   `Email:RemetenteEndereco` iguais ao endereço da conta — por padrão o Gmail não aceita
   remetente que não seja a conta autenticada.

**Remetente diferente do login, sem trocar de provedor: alias verificado.** Em *Ver todas as
configurações → Contas e importação → Enviar e-mail como*, adicionar o outro endereço e
confirmar o código que chega nele. Feito isso, `Email:RemetenteEndereco` pode divergir de
`Email:Usuario` e o Gmail aceita aquele `From`. ⚠️ **Alias não verificado falha calado**: o Gmail
não recusa o envio, apenas **reescreve** o remetente para a conta autenticada — a mensagem chega,
só que do endereço errado, e não sobra erro nenhum no log. Sondar por SMTP não resolve: o `MAIL
FROM` responde `250 OK` de qualquer jeito, porque o cabeçalho `From` só é conferido no `DATA`. A
única verificação que vale é olhar o "De:" de uma mensagem recebida de verdade.

Limites que importam: ~500 mensagens/dia, e é conta pessoal — serve para desenvolvimento,
portfólio e uso próprio, não para base real de usuários. O Gmail **envia para qualquer
destinatário**, não só para a própria conta; o problema não é técnico, é limite, reputação e
risco de suspensão da conta pessoal.

**Hotmail/Outlook pessoal não serve** como alternativa: a Microsoft descontinuou autenticação
básica nessas contas e exige OAuth 2.0, que o `SmtpClient` da BCL não fala (precisaria de MailKit
e do fluxo OAuth). Mais trabalho, limite menor, nenhum ganho.

**Caminho para a nuvem — sem domínio.** Serviços transacionais aceitam remetente verificado
avulso: Brevo (300/dia grátis), SendGrid (100/dia). A diferença estrutural em relação ao Gmail é
que **login SMTP e remetente deixam de ser a mesma coisa**: as credenciais são do provedor e o
`RemetenteEndereco` é o endereço verificado — então com domínio próprio depois muda-se só o
remetente. Como esses provedores oferecem relay SMTP, a migração é **só variável de ambiente**;
`EnviadorDeEmailSmtp` só precisaria ser trocada por um provedor exclusivamente por API. Ressalva:
remetente `@gmail.com` saindo de outro provedor não tem DKIM alinhado, então parte cai em spam —
domínio próprio com SPF/DKIM é o que resolve de fato. Os três cenários estão prontos e comentados
em `.env.exemplo`.

Callback a cadastrar em cada provedor (ajuste host/porta): `/signin-google`, `/signin-github`, `/signin-linkedin`.

## Fora de escopo por agora

- Deploy em nuvem (a imagem Docker existe e roda local; publicar num provedor é outro passo)
- ~~**Conteúdo** de outros exames além do AZ-900.~~ **Feito.** Os quatro exames estão declarados e **publicados** (27/08/2026), com pesos conferidos no study guide oficial de cada um:

| Exame | Domínios | Prova | Skills de | Questões escritas |
|---|---|---|---|---|
| AZ-900 | 3 | 40 em 45 min | — | 285 (publicado) |
| AZ-104 | 5 | 50 em 100 min | 17/04/2026 | 108 (5/5 domínios) |
| AZ-305 | 4 | 50 em 120 min | 17/04/2026 | 88 (4/4 domínios) |
| AZ-400 | 5 | 50 em 120 min | 27/07/2026 | 120 (5/5 domínios) |
| **AIF-C01** (AWS) | 5 | 65 em 90 min | v1.1, 30/04/2026 | 450 — 90/108/126/63/63 (publicado 15/09/2026) |

⚠️ No AZ-400, o domínio `pipelines` vale **50–55% sozinho** — um banco equilibrado entre os cinco daria prova enviesada, e por isso ele tem 32 questões contra 22 dos demais.

⚠️ **Profundidade é a dívida que sobra.** O AZ-900 tem 7,1× o tamanho da prova (285 para 40); os três novos ficam entre 1,8× e 2,4×. A partir da terceira tentativa do mesmo usuário o sorteio estoura o teto de 15% de repetidas — ele prefere repetir a entregar prova curta. Publicados assim por decisão consciente; crescer os bancos é o próximo passo de conteúdo, não um defeito de código.

⚠️ **As questões dos exames novos — inclusive as 450 do AIF-C01, geradas com assistência — não passaram por revisão técnica humana.** No AIF-C01 o risco maior está nos serviços recentes da v1.1 (Bedrock AgentCore, Strands Agents, Kiro, Amazon Quick, AWS Transform): o exame foi publicado a pedido do usuário antes dessa conferência, que continua pendente. O validador garante forma (contagem de alternativas, gabarito presente, explicação por distrator, distrator obrigatório no arrastar), não veracidade — gabarito errado passa em todos os testes.
  - **`Exame.IsPublished` ("em construção") continua valendo para o próximo exame.** O exame é semeado e recebe questões desde a primeira — os arquivos de seed exigem um `exameCode` declarado, então sem esse estado intermediário o exame só poderia entrar já pronto, e as centenas de questões seriam escritas sem nunca passar pelo seed nem pelos testes. Enquanto está em construção ele **some do catálogo e recusa tentativa**, e a recusa é na Application (`SessaoDeProvaService`), não só na tela: o id do exame trafega no formulário de "Iniciar simulado", então esconder o botão não impede quem já o tem. Publicar antes da hora **não quebra nada** — e é esse o problema: o sorteio faz `Math.Min(total, pool)` e entrega uma prova curta e sempre parecida, com cara de prova normal. Dois testes guardam o par, e o seed registra o progresso no log a cada startup. ⚠️ **Estudos de caso: risco reconhecido, NÃO confirmado.** Se AZ-305/AZ-400 tiverem case study (cenário longo compartilhado por várias questões), isso não cabe no modelo atual — `Questao` é unidade independente, e o formato exigiria entidade nova + migration, mudança no sorteio (o bloco é atômico e pode atravessar domínios, quebrando a repartição por cota) e uma UI de abas que colide com a navegação linear. **Mas os study guides oficiais dos dois exames não mencionam case studies**, e a afirmação anterior de que os têm era conhecimento não verificado apresentado como fato. Antes de tratar isso como épico, confirmar na página de detalhes do exame ou no `aka.ms/examdemo`. De todo modo não bloqueia escrever as questões independentes, que são a maior parte de qualquer um dos dois.
- **Alertas** (`alerting_rules` no Prometheus, Alertmanager, notificação do Grafana). A infraestrutura já suporta — `prephub_coleta_idade_seconds` e a taxa de 5xx são os dois candidatos naturais —, mas alerta sem destino combinado e sem alguém de plantão é só mais um painel vermelho que ninguém vê.
- **Logs e traces centralizados** (Loki, Tempo, OTLP). O `AddOpenTelemetry` já está montado e trocar o exportador é mudança de uma linha, mas isso dobraria a stack do compose para responder perguntas que hoje `docker compose logs` responde.