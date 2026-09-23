# PrepHub

Simulado preparatório para certificações **Microsoft Azure** e **AWS** que replica fielmente a experiência real da prova — interface, timer, navegação entre questões, marcação para revisão e score report na escala oficial.

O diferencial não é "mais um banco de questões": é a **fidelidade à experiência de prova** (estilo Pearson VUE), algo que o simulado oficial não oferece gratuitamente nesse nível.

## Exames disponíveis

| Certificação | Fornecedor | Questões |
|---|---|---|
| AZ-900 — Azure Fundamentals | Microsoft | ✅ |
| AZ-104 — Azure Administrator | Microsoft | ✅ |
| AZ-305 — Azure Solutions Architect | Microsoft | ✅ |
| AZ-400 — Azure DevOps Engineer | Microsoft | ✅ |
| AIF-C01 — AWS AI Practitioner | AWS | ✅ |

## Stack

- **.NET 10**, ASP.NET Core MVC
- **EF Core + SQLite**
- **xUnit** para testes
- **Docker** com compose (app + Prometheus + Grafana)
- Clean Architecture (Domain → Application → Infrastructure → Web)

## Começando

### Desenvolvimento local

```bash
dotnet build
dotnet run --project src/PrepHub.Web
# http://localhost:5090
```

### Docker

```bash
cp .env.exemplo .env          # credenciais de OAuth/SMTP (funciona vazio)
docker compose up --build
# http://localhost:8080        aplicação
# http://localhost:3000        Grafana (admin/admin)
# http://localhost:9090        Prometheus
```

A imagem se auto-inicializa: migrations e seed rodam no startup, então subir com volume vazio já cria o banco populado.

## Testes

```bash
dotnet test
```

## Autenticação

Login obrigatório para fazer simulado, com dois caminhos:

- **Conta local** — e-mail + senha, com confirmação por e-mail
- **Login social** — Google, LinkedIn, GitHub

Credenciais OAuth ficam em `dotnet user-secrets`, nunca no repositório:

```bash
cd src/PrepHub.Web
dotnet user-secrets init
dotnet user-secrets set "Authentication:Google:ClientId"     "..."
dotnet user-secrets set "Authentication:Google:ClientSecret" "..."
```

## Questões originais

Todas as questões são **originais**, escritas com base no Skills Measured outline público de cada exame. Nunca são usados dumps, questões vazadas ou conteúdo adaptado de practice assessments oficiais.

## Estrutura do projeto

```
PrepHub.sln
├── src/
│   ├── PrepHub.Domain            # Entidades e regras de negócio
│   ├── PrepHub.Application       # Casos de uso, interfaces, DTOs
│   ├── PrepHub.Infrastructure    # EF Core, DbContext, Repositories
│   └── PrepHub.Web               # ASP.NET Core MVC (Controllers, Views)
├── tests/
│   ├── PrepHub.Domain.Tests
│   ├── PrepHub.Application.Tests
│   ├── PrepHub.Infrastructure.Tests
│   └── PrepHub.Web.Tests
├── observabilidade/              # Prometheus + Grafana (configs versionadas)
├── tools/                        # Scripts auxiliares (backup, SMTP de teste)
└── assets/marca/                 # Arte-fonte da marca
```

## Observabilidade

Prometheus coleta métricas da aplicação (contas, provas, login, performance) e o Grafana as exibe em dashboards versionados. Tudo sobe junto com `docker compose up`.

## Licença

Projeto pessoal — uso livre para estudo.
