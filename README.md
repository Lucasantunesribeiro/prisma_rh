# Prisma RH

Plataforma B2B de gestão, cálculo, conferência e auditoria de folha de pagamento brasileira.

> **Estado atual: Fase 1 — Identidade, autenticação e multiempresa.**
>
> Existe login com JWT e refresh token, cinco perfis de acesso, e organizações
> isoladas entre si. **Nenhum cálculo de folha foi implementado ainda.** Consulte o
> [ROADMAP.md](ROADMAP.md) para as fases seguintes e o [CLAUDE.md](CLAUDE.md) para
> as regras do projeto.

---

## O que já existe

**Fundação (Fase 0)** — solução .NET em quatro projetos, `GET /health` que também
verifica o PostgreSQL, EF Core com Npgsql, PostgreSQL via Docker Compose, e um
frontend React + TypeScript + Vite + Tailwind + shadcn/ui.

**Identidade e multiempresa (Fase 1)**

- Organização, Usuário, Empresa e Estabelecimento, com CNPJ validado por dígito verificador.
- Login por e-mail e senha, com hash PBKDF2 (`PasswordHasher` do ASP.NET Core).
- Access token JWT de 15 minutos e refresh token de 7 dias em cookie `httpOnly`.
- Rotação do refresh a cada renovação, com **detecção de reúso**: um token já
  usado que reaparece derruba todas as sessões daquele usuário.
- Cinco perfis, com autorização aplicada **no backend**.
- Isolamento entre organizações por *global query filter* do EF Core.
- Telas de login, empresas e estabelecimentos.

## O que ainda NÃO existe

Funcionários, contratos, históricos, competências, rubricas, folha de pagamento,
cálculos, memória de cálculo, importações, motor de análises, integrações,
recursos AWS e CI/CD. Tudo isso pertence às fases seguintes do roadmap.

---

## Stack atual

| Camada | Tecnologia |
|---|---|
| Backend | C#, .NET 10, ASP.NET Core Web API (Minimal APIs) |
| Persistência | Entity Framework Core, Npgsql, PostgreSQL 17 |
| Autenticação | JWT (HMAC-SHA256) + refresh token opaco em cookie httpOnly |
| Documentação da API | OpenAPI |
| Testes backend | xUnit, Testcontainers |
| Frontend | React 19, TypeScript, Vite, React Router |
| Estilo | Tailwind CSS v4, shadcn/ui |
| Testes frontend | Vitest, Testing Library |
| Infra local | Docker Compose |

---

## Pré-requisitos

- [.NET SDK 10.0](https://dotnet.microsoft.com/download/dotnet/10.0) ou superior
- [Node.js 22](https://nodejs.org/) ou superior
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) com Compose v2 — usado
  pelo banco local **e** pelos testes de isolamento

```bash
dotnet --version
node --version
docker compose version
```

A versão do SDK exigida está fixada em [`global.json`](global.json).

---

## Estrutura do repositório

```text
prisma_rh/
├── backend/
│   ├── src/
│   │   ├── PrismaRH.Api/              # Minimal APIs, JWT, políticas, endpoints
│   │   ├── PrismaRH.Aplicacao/        # Casos de uso e portas (IContextoUsuario, ...)
│   │   ├── PrismaRH.Dominio/          # Entidades e regras, sem dependência externa
│   │   └── PrismaRH.Infraestrutura/   # EF Core, DbContext, JWT, hash de senha
│   ├── testes/PrismaRH.Testes/
│   │   ├── Dominio/                   # regras de domínio e CNPJ
│   │   └── Isolamento/                # multiempresa e autorização (Testcontainers)
│   └── PrismaRH.sln
├── frontend/src/
│   ├── api/                           # cliente HTTP, autenticação, empresas
│   ├── auth/                          # contexto de sessão
│   ├── paginas/                       # Entrar, Empresas, Estabelecimentos, Status
│   └── rotas/                         # guarda de rota
├── docker-compose.yml
├── .env.example
├── global.json
├── CLAUDE.md · ROADMAP.md · README.md
```

Dependências: `Api → Aplicacao → Dominio`, com `Infraestrutura` implementando as
portas. O projeto `Dominio` não referencia nada.

---

## 1. Configuração

```bash
cp .env.example .env
```

| Variável | Padrão | Para quê |
|---|---|---|
| `POSTGRES_DB` | `prisma_rh` | Nome do banco |
| `POSTGRES_USER` | `prisma_rh` | Usuário do banco |
| `POSTGRES_PASSWORD` | `prisma_rh_dev` | Senha de desenvolvimento |
| `POSTGRES_PORT` | `5433` | Porta exposta no host |
| `PRISMARH_SEED_SENHA` | `troque-esta-senha-de-demonstracao` | Senha dos usuários de demonstração |

> **Por que 5433?** Para não conflitar com uma instalação nativa de PostgreSQL
> ocupando a 5432.

A string de conexão e a chave JWT de desenvolvimento estão em
`backend/src/PrismaRH.Api/appsettings.Development.json`. **Fora de Development, as
duas precisam vir de variável de ambiente** — o `appsettings.json` base não contém
nenhuma credencial e a aplicação recusa iniciar sem elas:

```bash
ConnectionStrings__PrismaRh="Host=...;Port=5432;Database=...;Username=...;Password=..."
Jwt__ChaveAssinatura="chave-longa-e-aleatoria-de-no-minimo-32-caracteres"
```

---

## 2. Subir o PostgreSQL

```bash
docker compose up -d
docker compose ps          # espere ficar "healthy"
```

Para parar: `docker compose down` (preserva os dados) ou `docker compose down -v`
(**apaga** o volume).

---

## 3. Executar o backend

```bash
cd backend
dotnet restore
dotnet run --project src/PrismaRH.Api
```

A API sobe em `http://localhost:5080`. As migrations **não** são aplicadas
automaticamente; para aplicá-las:

```bash
dotnet ef database update --project src/PrismaRH.Infraestrutura --startup-project src/PrismaRH.Api
```

Na primeira execução em Development, com o banco vazio e `PRISMARH_SEED_SENHA`
definida, são criadas duas organizações fictícias. A semeadura é idempotente e
não roda fora de Development.

### Usuários de demonstração

Todos usam a senha de `PRISMARH_SEED_SENHA`.

| E-mail | Perfil | Organização |
|---|---|---|
| `plataforma@prisma.exemplo` | Administrador da Plataforma | Prisma |
| `admin@prisma.exemplo` | Administrador da Empresa | Prisma |
| `analista@prisma.exemplo` | Analista de RH | Prisma |
| `auditor@prisma.exemplo` | Auditor | Prisma |
| `visualizador@prisma.exemplo` | Visualizador | Prisma |
| `admin@horizonte.exemplo` | Administrador da Empresa | **Horizonte** |

> A segunda organização existe para você **ver o isolamento funcionando**: entre
> como `admin@horizonte.exemplo` e repare que nenhuma empresa da Prisma aparece.

### Endpoints

| Rota | Perfis | Descrição |
|---|---|---|
| `GET /health` | anônimo | Estado da API e do banco |
| `GET /openapi/v1.json` | anônimo | OpenAPI (só em Development) |
| `POST /api/autenticacao/entrar` | anônimo | Login |
| `POST /api/autenticacao/renovar` | cookie | Rotaciona a sessão |
| `POST /api/autenticacao/sair` | cookie | Revoga o refresh |
| `GET /api/autenticacao/eu` | autenticado | Usuário do token |
| `GET /api/empresas` | todos os 5 | Lista da própria organização |
| `POST`/`PUT`/`DELETE /api/empresas` | Adm. Plataforma, Adm. Empresa | Administra |
| `GET /api/empresas/{id}/estabelecimentos` | todos os 5 | Lista |
| `POST`/`PUT`/`DELETE` de estabelecimentos | Adm. Plataforma, Adm. Empresa | Administra |

---

## 4. Executar o frontend

```bash
cd frontend
npm install
npm run dev
```

Sobe em `http://localhost:5173`. Por padrão aponta para `http://localhost:5080`;
para mudar, copie `frontend/.env.example` para `frontend/.env` e ajuste `VITE_API_URL`.

---

## 5. Testes

### Backend

```bash
cd backend
dotnet test
```

Duas famílias, com necessidades diferentes:

- **Domínio e contrato** — não dependem de banco. Os testes do `/health` usam de
  propósito uma conexão inacessível, para provar que a API sobe e responde mesmo
  com o banco fora.
- **Isolamento e autorização** — sobem um **PostgreSQL real via Testcontainers**.
  Exigem Docker rodando. Filtro global testado contra banco falso não prova nada:
  o EF InMemory nem gera SQL.

### Frontend

```bash
cd frontend
npm run test        # execução única
npm run lint
npm run build       # verificação de tipos + build de produção
```

---

## Decisões de segurança

Quatro escolhas que valem conhecer antes de mexer:

**O `IdOrganizacao` só vem do token.** Nunca do corpo, da query string ou de
header. Enviar `idOrganizacao` numa requisição não tem efeito — existe teste
provando isso.

**O isolamento não depende de lembrar.** O filtro por organização é global no
`PrismaRhDbContext`: toda consulta já nasce restrita. Sem usuário autenticado o
valor é `Guid.Empty`, que não casa com nada — o sistema falha **fechado**.
Atravessar a fronteira exige `IgnoreQueryFilters()` explícito.

**Recurso de outra organização devolve 404, não 403.** Um 403 confirmaria que
aquele id existe e permitiria mapear os dados do vizinho um id por vez.

**O access token nunca toca o `localStorage`.** Vive só em memória no React; o
refresh mora num cookie `httpOnly` que o JavaScript não lê. Um XSS não rouba a
sessão. Ao recarregar a página, a sessão é restaurada pelo cookie.

### Limite conhecido

A validação de CNPJ cobre apenas o formato **numérico**. A transição para CNPJ
alfanumérico da Receita Federal **não** foi implementada: exige fonte oficial
confirmada, conforme `CLAUDE.md §29`. O ponto está isolado em
`PrismaRH.Dominio/Empresas/Cnpj.cs`.

---

## Ordem para subir tudo

```bash
cp .env.example .env
docker compose up -d

# terminal 1
cd backend && dotnet run --project src/PrismaRH.Api

# terminal 2
cd frontend && npm install && npm run dev
```

Abra `http://localhost:5173` e entre com um dos usuários da tabela acima.
