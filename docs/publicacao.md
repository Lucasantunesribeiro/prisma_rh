# Publicação do Prisma RH

O CI ([`.github/workflows/ci.yml`](../.github/workflows/ci.yml)) **testa**, mas não
publica. Publicar é manual, por este roteiro — e cada passo tem a sua reversão escrita
**antes** de ser executado.

> **Autorização.** Pelo `CLAUDE.md §31`, publicação só acontece com autorização explícita na
> tarefa corrente. Este documento diz **como**; ele não autoriza nada.

## O que existe em produção

| Peça | Onde | Como chega lá |
|---|---|---|
| Frontend | Vercel, projeto `portfolio-prisma-rh` | `npx vercel --prod` em `frontend/` (pasta já ligada ao projeto) |
| API | Lambda `portfolio-prisma-rh-prod-api`, `provided.al2023`, Function URL | zip de `scripts/empacotar-lambda.py api` |
| Worker | Lambda `portfolio-prisma-rh-worker-importacao`, acionada pela SQS | zip de `scripts/empacotar-lambda.py worker` |
| Banco | Neon PostgreSQL | migrations manuais, **antes** do código que precisa delas |
| Segredos | SSM Parameter Store (`SecureString`) | a Lambda recebe só o **nome** do parâmetro |

## ⚠️ Duas armadilhas desta máquina, sempre explícitas

```text
--profile portfolio-claude --region us-east-1
```

Em **todo** comando `aws`. Há `AWS_ACCESS_KEY_ID` de outro projeto no ambiente, e variável
de ambiente vence `AWS_PROFILE`. E `AWS_DEFAULT_REGION` aponta para `sa-east-1`: sem
`--region`, o comando vai para a região errada **e funciona**, criando recurso onde ninguém
procura (já aconteceu — `CLAUDE.md §24.19`, item 9).

## Ordem

### 1. Tudo verde localmente

```bash
dotnet build backend/PrismaRH.sln -c Release -warnaserror
dotnet test backend/PrismaRH.sln -c Release --no-build
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test backend/PrismaRH.sln -c Release --no-build
cd frontend && npm ci && npm run lint && npm run test && npm run build && npm audit --audit-level=high
```

### 2. Push e CI verde do commit — antes de qualquer outra coisa

```bash
git push origin main
gh run list --commit "$(git rev-parse HEAD)"
gh run watch <id-do-run> --exit-status
```

CI vermelha **bloqueia** a publicação: corrige-se em commit novo e repete-se. Nada vai para
a AWS ou para a Vercel antes deste passo.

### 3. Migrations, se houver

```bash
dotnet ef migrations has-pending-model-changes --project backend/src/PrismaRH.Infraestrutura --startup-project backend/src/PrismaRH.Api
```

Se o commit trouxer migration nova, ela é aplicada no Neon **antes** do código novo — o
código antigo convive com a coluna nova; o código novo não convive com a coluna faltando.
O `/health` da API acusa migration pendente.

### 4. Guardar o pacote que está no ar (é a reversão)

```bash
aws lambda get-function --function-name portfolio-prisma-rh-prod-api \
  --profile portfolio-claude --region us-east-1 \
  --query "{url:Code.Location,sha:Configuration.CodeSha256}"
```

Baixar o zip pela URL devolvida (vale alguns minutos) e guardar fora do repositório,
junto do `CodeSha256`. Repetir para o worker.

### 5. Empacotar

```bash
python scripts/empacotar-lambda.py api
python scripts/empacotar-lambda.py worker
```

O script **recusa** continuar se o `bootstrap` não for binário Linux (ELF), grava o zip com
origem Unix e modo `0755`, deixa `appsettings.Development.json` de fora e relê o pacote
para conferir. Os motivos estão no cabeçalho do próprio script.

### 6. Publicar e esperar a atualização terminar

```bash
aws lambda update-function-code --function-name portfolio-prisma-rh-prod-api \
  --zip-file fileb://.publicacao/api.zip \
  --profile portfolio-claude --region us-east-1

aws lambda wait function-updated-v2 --function-name portfolio-prisma-rh-prod-api \
  --profile portfolio-claude --region us-east-1

aws lambda get-function-configuration --function-name portfolio-prisma-rh-prod-api \
  --profile portfolio-claude --region us-east-1 \
  --query "{estado:LastUpdateStatus,sha:CodeSha256}"
```

`LastUpdateStatus` precisa ser `Successful`. Repetir para o worker.

### 7. Frontend

```bash
cd frontend
npx vercel --prod
```

As variáveis (`VITE_API_URL`, `VITE_DEMO_SENHA`) já estão no projeto da Vercel. Variável
com prefixo `VITE_` **vai para o bundle**: nunca pôr segredo nela.

### 8. Fumaça

- `GET <api>/health` → `{"status":"saudavel"}`;
- o frontend abre, a conta de demonstração entra pelo botão, o Painel mostra o roteiro;
- Tab até "Abrir holerite", Enter abre, Escape fecha e o foco volta;
- console do navegador sem erro.

## Reverter

| Passo | Reversão |
|---|---|
| Lambda (API ou worker) | `update-function-code` com o zip guardado no passo 4, e `wait function-updated-v2` |
| Frontend | `npx vercel rollback` ou promover o deployment anterior no painel da Vercel |
| Commit | commit de reversão (`git revert`), nunca force push |
| Migration | só com `Down` revisado e backup do Neon — ver [backup-e-restore.md](backup-e-restore.md) |

## Custo

Publicar código numa Lambda existente não cria recurso e não muda memória nem timeout:
**US$ 0,00**. Qualquer recurso AWS novo segue o `CLAUDE.md §16` — preço vigente conferido,
nenhuma cobrança por existência, e autorização antes.
