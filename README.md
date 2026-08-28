# Prisma RH

Plataforma B2B de gestão, cálculo, conferência e auditoria de folha de pagamento brasileira.

> **Estado atual: Fase 4D concluída — IRRF.**
>
> Existe login com JWT, cinco perfis, organizações isoladas entre si, o cadastro de
> funcionários, contratos e histórico contratual por vigência, e a **primeira folha
> mensal calculada e armazenada pelo próprio Prisma RH**, com memória de cálculo,
> reprocessamento e fechamento.
> As bases de INSS, FGTS e IRRF são apuradas por holerite, o **INSS do segurado é
> descontado** pela tabela progressiva vigente e o **FGTS do empregador é depositado**
> a 8% sobre a base — como linha informativa, que não reduz o líquido.
> O **IRRF** é retido pela tabela de 2026, com dedução por dependente, desconto
> simplificado e o **redutor** da Lei 15.270/2025 — quem ganha até R$ 5.000 não paga.
> Com isso a folha mensal está completa: salário proporcional, bases, INSS, FGTS e IRRF.
> Consulte o [ROADMAP.md](ROADMAP.md) para as fases seguintes e o
> [CLAUDE.md](CLAUDE.md) para as regras do projeto.

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

**Cadastro funcional (Fase 2)**

- Funcionário (a pessoa) e ContratoTrabalho (o vínculo), um para muitos: readmissão
  cria contrato novo sem apagar o anterior.
- CPF validado por dígito verificador, **mascarado na listagem** e completo só no detalhe.
- Catálogo de cargos por organização.
- **Histórico por vigência**: salário, cargo, estabelecimento e jornada juntos num
  período com `valido_de` e `valido_ate`. Toda alteração fecha a vigência anterior na
  véspera e abre outra — nada é sobrescrito.
- `GET /api/contratos/{id}/vigencia?data=` responde *"o que valia nesta data"*, que é a
  pergunta que o motor de cálculo da Fase 3 vai fazer.
- Telas de funcionários com filtro, linha do tempo do contrato e catálogo de cargos.

**Núcleo da folha mensal (Fase 3)**

- **Competência** como tipo próprio (`08/2026`), persistida como o inteiro `202608`
  para ordenar e indexar numa coluna só.
- Catálogo de **rubricas** por organização: provento, desconto ou informativo; o valor
  ou é calculado pelo sistema (salário-base) ou digitado no lançamento. Não há fórmula
  em texto livre — parametrização não executa código do usuário (`CLAUDE.md §9`).
- **Motor de cálculo** determinístico e sem acesso a banco: salário-base proporcional
  em **30 avos** (CLT art. 64), repartido por vigência quando há alteração no meio do
  mês. Fevereiro inteiro vale 30 avos; o dia 31 não vale um avo extra.
- Entra na folha quem teve vínculo em **qualquer dia** da competência — admitido dia 20
  entra com 11/30, desligado dia 10 entra com 10/30.
- **Lançamentos manuais** de provento e desconto, que **sobrevivem ao recálculo**:
  reprocessar refaz só o que o sistema calculou.
- **Memória de cálculo** por lançamento, em passos: a conta que produziu o número, não
  só o número. Cada lançamento congela código, nome e tipo da rubrica no momento do
  cálculo.
- Fechamento definitivo: folha fechada recusa cálculo, lançamento e novo fechamento.
- Arredondamento centralizado: 2 casas, `AwayFromZero`, aplicado na parcela.
- Telas de rubricas, lista de folhas e detalhe da folha com holerite e memória.

**Incidências e bases (Fase 4A)**

- Cada rubrica declara **em quais bases entra**: INSS, FGTS e IRRF. É um enum de bits
  numa coluna só — `Inss | Fgts` guarda 3 —, e um teste exige que todo valor seja
  potência de dois, porque numerar em sequência faria o terceiro colidir com a
  combinação dos dois primeiros.
- O holerite passa a apurar as **três bases**, com os códigos das rubricas que formaram
  cada uma. Essa composição é **derivada**, não gravada: cada lançamento já carrega a
  incidência, então dizer quais entraram é filtrar o que já está lá.
- **Rubrica de desconto não compõe base** — o construtor recusa e a API devolve 400.
  Base de INSS é soma de proventos; desconto não a reduz. O que reduz base é *dedução*,
  que é outro conceito e pertence à Fase 4D.
- A incidência é **congelada no lançamento**, como código, nome e tipo já eram. Mudar a
  incidência de uma rubrica não mexe em holerite já calculado; vale a partir do próximo.

**INSS progressivo (Fase 4B)**

- **Tabela por vigência**, com a fonte oficial como campo obrigatório: a construção é
  recusada sem ela (`CLAUDE.md §29`). Cadastrar 2027 é um `POST` com a vigência nova —
  **o algoritmo não muda**.
- **Cálculo progressivo de verdade**: cada trecho da base paga a alíquota da sua faixa.
  Quem ganha R$ 5.000 não paga 14% sobre tudo. Existe teste exigindo que o resultado
  **não** seja o da conta errada.
- **Teto**: base acima do limite não aumenta a contribuição, e o corte aparece na memória.
- **Memória faixa a faixa**: base, cada trecho com sua alíquota e o valor exato, e o total.
- A tabela é escolhida pelo **primeiro dia da competência** — folha histórica usa os
  parâmetros da própria competência, não a mais recente.
- O desconto é reapurado ao calcular, ao **lançar** e ao **remover**: adicionar uma
  comissão não deixa a contribuição parada no valor antigo.
- `tabelas_inss` não tem organização, porque INSS é lei
  federal. Todos leem; só o Administrador da Plataforma escreve.

> ⚠️ **Pendência legal registrada:** nenhuma fonte oficial alcançada declara em **qual
> etapa** o INSS é arredondado. Adotou-se o critério do projeto — arredondar uma vez, no
> valor final da rubrica — explicitamente como escolha de engenharia, não como afirmação
> jurídica. Detalhes e o impacto numérico estão na Fase 4B do [ROADMAP.md](ROADMAP.md).

**FGTS (Fase 4C)**

Fonte: **Lei nº 8.036/1990, art. 15** — 8% da remuneração, registrada como campo
obrigatório da tabela.

- **FGTS não é desconto.** É depósito do empregador: entra no holerite como rubrica
  **informativa**, aparece em coluna própria e **não reduz o líquido de ninguém**. O
  domínio recusa uma rubrica de FGTS que não seja informativa — modelá-la como desconto
  tiraria 8% do salário de todo funcionário, e o holerite **continuaria fechando**.
- **A rubrica de FGTS não compõe base alguma.** A guarda é explícita porque informativo
  *pode* compor base; se o FGTS entrasse na base de FGTS, cada cálculo aumentaria a base
  do seguinte — 3.000 → 3.240 → 3.499,20 — sem que nenhuma linha parecesse errada.
- **Alíquota única e sem teto**, ao contrário do INSS: quem ganha R$ 20.000 recolhe INSS
  sobre R$ 8.475,55 e FGTS sobre os R$ 20.000 inteiros.
- **Alíquota como fração**, nunca percentual: `0.08`. O construtor recusa `8`, que
  depositaria oito vezes o salário.
- **Uma rubrica de FGTS ativa por organização**, garantida por índice único parcial. Duas
  dobrariam a guia — e aqui o erro seria pior que o do INSS, porque o holerite continuaria
  fechando certo.
- O depósito é reapurado ao calcular, ao **lançar** e ao **remover**, como o INSS.
- `tabelas_fgts` também não tem organização, pelo mesmo motivo: é lei federal.

> **Limitações declaradas:** a alíquota de **2% do contrato de aprendizagem** não é
> suportada — o contrato não tem campo que identifique aprendizagem, e criá-lo sairia do
> escopo da subfase. A **multa rescisória de 40%** e o FGTS sobre 13º e férias pertencem
> às Fases 4E, 4F e 4G, que introduzem essas verbas.

**IRRF (Fase 4D)**

Fontes: **Lei nº 15.191/2025** (tabela) e **Lei nº 15.270/2025** (redutor), publicadas
pela Receita Federal. Os **cinco exemplos numéricos oficiais** de aplicação da Lei
15.270/2025 estão reproduzidos como testes — eles não provam que o código faz o que o
autor quis, provam que ele faz o que a Receita publicou.

Três diferenças em relação ao INSS, cada uma com teste próprio:

1. **Não é soma trecho a trecho.** Aplica-se **uma** alíquota — a da faixa onde a base
   caiu — sobre a base **inteira**, e subtrai-se a **parcela a deduzir**. O resultado é
   equivalente hoje; a fórmula não é.
2. **A base não é a remuneração.** É a remuneração menos as deduções, e há duas formas
   que **não se somam**: `rendimentos − INSS − dependentes` ou `rendimentos − 607,20`.
   Vale a que der a **menor** base. O desconto simplificado substitui todas as deduções
   legais, inclusive o INSS.
3. **Existe redutor**: `978,62 − 0,133145 × rendimentos brutos`, aplicado sobre o imposto
   já apurado e **limitado a ele** — zera, nunca restitui. É o mecanismo que isentou quem
   ganha até R$ 5.000 sem mexer nas faixas.

- O IRRF é apurado **por último**, e a ordem não é estética: ele **deduz o INSS** que a
  mesma folha acabou de apurar, e lê esse valor do lançamento — não de um campo em
  memória, porque o holerite pode ter vindo do banco.
- A **quantidade de dependentes é congelada** no holerite. Cadastrar um filho hoje não
  muda o imposto de uma folha fechada em março.
- A **última faixa tem limite nulo**, não um número gigante: o IRRF não tem teto, e *"o
  maior número que existe"* não é a mesma afirmação que *"não há limite"*.
- IRRF é **desconto** — o espelho exato do FGTS, que é informativo.

> **Limitações declaradas:** só a folha **mensal**. IRRF sobre 13º (tributação
> exclusiva), férias e rescisão pertencem às Fases 4E–4G. Pensão alimentícia,
> previdência privada e parcela isenta acima de 65 anos não têm dado de origem no
> domínio ainda. Não há ajuste anual: o produto retém na fonte.

**Dependentes (Fase 4D, etapa 1)**

- Pertencem à **pessoa**, não ao contrato: um filho continua sendo filho se ela for
  readmitida com contrato novo.
- **Cadastrar não faz o imposto cair.** Só abate IRRF quem tem **período declarado**, e a
  tela mostra isso numa coluna própria.
- A dedutibilidade é **declarada, não derivada da idade**. A regra legal — 21 anos, 24 se
  estudante — não está codificada, porque exige fonte oficial que o projeto ainda não tem
  (`CLAUDE.md §29`). Derivar produziria um número que parece autoritativo e não é.
- A dedução vale pelo **mês inteiro**: quem passa a contar no dia 20 conta o mês todo.
- Rotas **aninhadas** no funcionário — o dependente é resolvido pelo pai, que já passa pelo
  filtro global. É o que fecha o IDOR sem depender de conferência manual.
- **Sem CPF do dependente**: o cálculo mensal não precisa dele, e guardar documento de
  terceiro sem uso seria coletar por precaução (`CLAUDE.md §25`).
- Teto de **30 por funcionário** — limite de recurso, não regra legal.

## O que ainda NÃO existe

Férias, 13º, rescisão, afastamentos, importações, motor de análises, integrações,
recursos AWS e CI/CD. Tudo isso pertence às fases seguintes do roadmap.

Cada encargo exige fonte oficial registrada e parâmetro versionado (`CLAUDE.md §29`). A
Fase 4A criou as bases, a 4B aplicou a primeira alíquota, a 4C acrescentou o depósito do
empregador e a 4D fechou o imposto de renda. **A folha mensal está completa**; os demais
tipos de processamento — férias (4E), 13º (4F) e rescisão (4G) — continuam fora.

### Camada de IA — planejada, não implementada

Está previsto um **assistente inteligente** para a **Fase 11** do roadmap: explicar
inconsistências em linguagem simples, resumir uma folha já processada e converter
perguntas em português em consultas controladas pela aplicação.

**Nada disso existe hoje.** Não há SDK, endpoint, chave de provedor nem chamada a
modelo no repositório — e o provedor sequer foi escolhido.

Quando existir, a IA será uma camada **complementar e de leitura**: o motor de cálculo
continuará 100% determinístico em C#, e nenhum valor financeiro ou obrigação legal terá
origem num modelo de linguagem. O escopo, as restrições e os critérios de aceite estão
na Fase 11 do [ROADMAP.md](ROADMAP.md); as regras permanentes, no `CLAUDE.md §37`.

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
| Estilo | Tailwind CSS v4, shadcn/ui, Radix, Lucide |
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
| `GET /api/cargos` | todos os 5 | Catálogo de cargos |
| `POST`/`PUT`/`DELETE /api/cargos` | + Analista de RH | Administra |
| `GET /api/funcionarios` | todos os 5 | Lista, com filtro por nome, CPF e situação |
| `GET /api/funcionarios/{id}` | todos os 5 | Detalhe, com CPF completo |
| `POST`/`PUT /api/funcionarios` | + Analista de RH | Administra |
| `GET`/`POST /api/funcionarios/{id}/contratos` | ler / + Analista | Contratos da pessoa |
| `GET /api/contratos/{id}/vigencias` | todos os 5 | Histórico completo |
| `GET /api/contratos/{id}/vigencia?data=` | todos os 5 | O que valia numa data |
| `POST /api/contratos/{id}/vigencias` | + Analista de RH | Registra alteração |
| `POST /api/contratos/{id}/desligamento` | + Analista de RH | Encerra o vínculo |
| `GET /api/rubricas` | todos os 5 | Catálogo de rubricas |
| `POST`/`PUT`/`DELETE /api/rubricas` | Adm. Plataforma, Adm. Empresa | Parametrização |
| `PUT /api/rubricas/{id}/incidencias` | Adm. Plataforma, Adm. Empresa | Em quais bases a rubrica entra |
| `GET /api/tabelas-inss` | todos os 5 | Tabelas de INSS por vigência, com as faixas |
| `POST /api/tabelas-inss` | **só Adm. Plataforma** | Cadastra a vigência de um ano novo |
| `GET /api/tabelas-fgts` | todos os 5 | Alíquotas de FGTS por vigência |
| `POST /api/tabelas-fgts` | **só Adm. Plataforma** | Cadastra uma vigência nova de alíquota |
| `GET /api/tabelas-irrf` | todos os 5 | Tabelas de IRRF por vigência, com faixas e redutor |
| `POST /api/tabelas-irrf` | **só Adm. Plataforma** | Cadastra a vigência de um ano novo |
| `GET /api/funcionarios/{id}/dependentes` | todos os 5 | Dependentes da pessoa |
| `POST /api/funcionarios/{id}/dependentes` | Adm. Empresa, Analista | Cadastra dependente |
| `PUT /api/funcionarios/{id}/dependentes/{idDep}` | Adm. Empresa, Analista | Altera dados e período de dedução |
| `DELETE /api/funcionarios/{id}/dependentes/{idDep}` | Adm. Empresa, Analista | Remove dependente |
| `GET /api/folhas` | todos os 5 | Lista, com filtro por empresa e competência |
| `GET /api/folhas/{id}` | todos os 5 | Folha com os holerites |
| `GET /api/folhas/{id}/funcionarios/{idHolerite}` | todos os 5 | Holerite, memória de cálculo e bases |
| `POST /api/folhas` | + Analista de RH | Abre a folha de uma competência |
| `POST /api/folhas/{id}/calcular` | + Analista de RH | Calcula ou recalcula |
| `POST /api/folhas/{id}/fechar` | + Analista de RH | Fecha em definitivo |
| `POST` de lançamento manual | + Analista de RH | Lança provento ou desconto |
| `DELETE` de lançamento manual | + Analista de RH | Remove o que foi digitado |

> **"+ Analista de RH"** significa Administrador da Plataforma, Administrador da
> Empresa **e** Analista de RH. O Analista mantém cadastros mas **não** administra
> empresas — por isso a política de pessoas é separada da de empresas.
>
> **Rubrica** é parametrização da empresa, não operação do dia a dia: quem cria e
> inativa rubrica é quem administra empresas (`CLAUDE.md §6`). O Analista de RH
> processa a folha, mas não muda o catálogo com que ela é calculada.

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

## Interface

O frontend usa um **application shell único**: sidebar com contexto de empresa,
topbar com breadcrumb e um workspace que ocupa a largura disponível. Todas as
telas — atuais e futuras — seguem o mesmo sistema.

A direção é *data-first*: tabelas como componente de primeira classe, resumo
financeiro em faixa tipográfica em vez de grade de cards, valores monetários
com algarismo de largura fixa, e detalhe em painel lateral (o holerite abre a
memória de cálculo sem sair da folha).

**A fonte de verdade visual é o [DESIGN.md](DESIGN.md)**, obrigatório para
qualquer tela nova. Ele traz a paleta, a tipografia, o shell, os componentes
compartilhados e a lista do que é proibido.

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

**CPF só aparece inteiro no detalhe.** Na listagem vem mascarado (`111.***.**7-35`).
A busca por CPF exige o documento completo e válido — busca parcial viraria uma forma
de descobrir documentos por tentativa.

**O passado não é reescrito.** Registrar um aumento fecha a vigência anterior e abre
uma nova; o salário antigo continua consultável. Duas garantias sustentam isso: a
invariante no agregado `ContratoTrabalho` e uma **constraint de exclusão no
PostgreSQL** que impede qualquer sobreposição de períodos, mesmo sob requisições
simultâneas.

**O access token nunca toca o `localStorage`.** Vive só em memória no React; o
refresh mora num cookie `httpOnly` que o JavaScript não lê. Um XSS não rouba a
sessão. Ao recarregar a página, a sessão é restaurada pelo cookie.

### Sobre as credenciais de desenvolvimento neste repositório

A senha do PostgreSQL local (`prisma_rh_dev`) e a chave JWT de desenvolvimento estão
versionadas de propósito, e isso é uma escolha, não um descuido:

- o banco é um container efêmero que só existe em `localhost`;
- a chave diz literalmente `nao-usar-em-producao` e serve para o `dotnet run` funcionar
  sem configuração manual;
- **fora de Development a aplicação recusa iniciar** sem `Jwt__ChaveAssinatura` e
  `ConnectionStrings__PrismaRh` vindos do ambiente — o `appsettings.json` base não tem
  nenhuma credencial.

A senha dos usuários de demonstração **não** está no repositório: vem de
`PRISMARH_SEED_SENHA`, e sem ela a semeadura não roda.

### Segurança é requisito de todas as fases

Desde 27/08/2026, **toda fase do roadmap tem um Security Gate obrigatório** — ameaças
introduzidas, controles, testes, impacto multiempresa, exposição de dados, permissões,
auditoria, dependências, secrets, superfície pública e risco de abuso. A fase de
Hardening continua existindo como **auditoria final e pentest controlado**, não como o
momento em que segurança começa.

O modelo completo está em [`CLAUDE.md §24`](CLAUDE.md); os gates por fase, em
[`ROADMAP.md`](ROADMAP.md).

Este projeto **não afirma ser "100% seguro"** — nenhum sistema pode. O objetivo é reduzir
superfície de ataque, eliminar vulnerabilidades de classes conhecidas, detectar
comportamento anormal, dificultar exploração e permitir recuperação.

### Limitações conhecidas

Registradas de propósito, porque limitação conhecida e datada vale mais que falsa
completude.

**Não existe rate limiting.** O login já é constante no tempo e não enumera usuários,
mas nada impede tentativas em massa. Aceitável em `localhost`; **bloqueante antes do
primeiro deploy público** (requisito de saída da Fase 10).

**O cookie `SameSite=Lax` não sobrevive à topologia planejada de produção.** Frontend na
Vercel e API no API Gateway são domínios diferentes; o navegador não enviará o cookie, e
a sessão morre ao recarregar. A correção óbvia (`SameSite=None`) reabre o CSRF que o
`Lax` fechava de graça — por isso a decisão precede o deploy, e está descrita no Security
Gate da Fase 10.

**Nem toda listagem tem paginação.** Empresas e funcionários têm teto de 100 por página;
folhas, rubricas, cargos, estabelecimentos, holerites e lançamentos ainda devolvem tudo.

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
