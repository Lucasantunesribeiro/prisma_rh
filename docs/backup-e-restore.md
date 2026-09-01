# Backup e restauração

> **Este documento descreve um exercício que foi executado, não um plano.** Os números
> abaixo são os obtidos em **01/09/2026**.
>
> `CLAUDE.md §24.23` é explícito sobre por que isso importa: **backup nunca testado é
> hipótese, não garantia.** Um procedimento escrito que ninguém rodou costuma falhar
> exatamente no dia em que é necessário.

## O que foi provado

| # | Etapa | Resultado |
|---|---|---|
| 1 | Backup com `pg_dump -Fc` | 125.154 bytes |
| 2 | Ambiente **isolado** — container próprio, porta 5434, volume próprio | subiu em 3 s |
| 3 | `pg_restore` | sem erro |
| 4 | **Aplicação iniciada contra a base restaurada** | `/health` respondeu `saudavel`, com o *check* de banco incluso |
| 5 | Autorização continua valendo na base restaurada | `GET /api/empresas` sem token → **401** |
| 6 | Migrations lidas pelo EF Core | 19 aplicadas, 1 pendente — exatamente a criada naquele dia |
| 7 | `dotnet ef database update` sobre a base restaurada | 20 aplicadas, 3 colunas novas, **6 usuários preservados** |

## Conferência estrutural — origem × restaurado

Comparação **tabela a tabela**, não por amostragem:

| | Origem | Restaurado | |
|---|---|---|---|
| Tabelas | 34 | 34 | ✅ |
| Contagem de linhas por tabela | — | — | ✅ **nenhuma diferença** |
| Constraints | 81 | 81 | ✅ |
| Índices | 98 | 98 | ✅ |
| Colunas | 285 | 285 | ✅ |

> ⚠️ **A constraint que mais importa sobreviveu:** `ex_vigencias_sem_sobreposicao`, a
> *exclusion constraint* que impede vigências contratuais sobrepostas.
>
> Ela é o caso em que contar linhas não bastaria: um restore que a perdesse deixaria a base
> **estruturalmente mais fraca** sem que nenhuma contagem acusasse — e o defeito só
> apareceria meses depois, como duas vigências sobrepostas que o C# deixou passar sob
> concorrência.

## O procedimento

⚠️ **Nada aqui toca a base de produção de forma destrutiva.** `pg_dump` é leitura; a
restauração acontece num container descartável, com porta e volume próprios.

### 1. Backup

```bash
docker exec prisma-rh-postgres \
  pg_dump -U prisma_rh -d prisma_rh -Fc -f /tmp/prisma_rh.dump

docker cp prisma-rh-postgres:/tmp/prisma_rh.dump ./prisma_rh.dump
```

**`-Fc` (custom), e não SQL puro**, por três razões: restaura seletivamente, comprime, e
permite `pg_restore --list` para inspecionar o conteúdo sem restaurar nada.

Em produção o backup é do provedor (Neon). Este procedimento vale igual: o que muda é a
origem do dump.

### 2. Ambiente isolado

```bash
docker run -d --name prisma-rh-restore \
  -e POSTGRES_DB=prisma_rh_restore \
  -e POSTGRES_USER=restore \
  -e POSTGRES_PASSWORD=<senha efemera, so deste exercicio> \
  -p 5434:5432 postgres:17-alpine
```

**Porta 5434, e não 5433.** A diferença de um dígito é o que impede o restore de escrever
por cima do banco de desenvolvimento — e é o tipo de erro que só se comete uma vez.

### 3. Restauração

```bash
docker cp ./prisma_rh.dump prisma-rh-restore:/tmp/prisma_rh.dump

docker exec prisma-rh-restore \
  pg_restore -U restore -d prisma_rh_restore --no-owner --no-privileges \
  /tmp/prisma_rh.dump
```

`--no-owner --no-privileges` porque o usuário de origem não existe no destino — sem isso o
`pg_restore` acumula erros de `ALTER OWNER` que escondem os erros de verdade.

### 4. Conferência

```bash
# Contagem por tabela, nos dois bancos, e diff.
CONTA="select table_name, (xpath('/row/c/text()',
  query_to_xml(format('select count(*) as c from %I.%I', table_schema, table_name),
  false, true, '')))[1]::text::int from information_schema.tables
  where table_schema='public' order by table_name;"

docker exec prisma-rh-postgres psql -U prisma_rh -d prisma_rh -tAF'|' -c "$CONTA" | sort > origem.txt
docker exec prisma-rh-restore  psql -U restore   -d prisma_rh_restore -tAF'|' -c "$CONTA" | sort > restaurado.txt
diff origem.txt restaurado.txt
```

E a estrutura:

```sql
select count(*) from pg_constraint where connamespace='public'::regnamespace;
select count(*) from pg_indexes where schemaname='public';
select conname from pg_constraint where contype='x';   -- exclusion constraints
```

### 5. A aplicação contra a base restaurada

**Esta é a etapa que separa "o dump existe" de "o backup serve".**

```bash
cd backend
ASPNETCORE_ENVIRONMENT=Development \
ConnectionStrings__PrismaRh="Host=localhost;Port=5434;Database=prisma_rh_restore;Username=restore;Password=<senha efemera>" \
ASPNETCORE_URLS="http://localhost:5099" \
dotnet run --project src/PrismaRH.Api

curl http://localhost:5099/health
curl -o /dev/null -w "%{http_code}\n" http://localhost:5099/api/empresas   # 401 esperado
```

### 6. Trazer o backup ao dia

```bash
ConnectionStrings__PrismaRh="Host=localhost;Port=5434;..." \
dotnet ef database update --project src/PrismaRH.Infraestrutura --startup-project src/PrismaRH.Api
```

Um backup antigo restaurado num código novo **precisa** aceitar as migrations seguintes.
Se não aceitar, o backup só serve para o dia em que foi tirado.

### 7. Desmontar

```bash
docker rm -f prisma-rh-restore
```

O container descartável não deve sobreviver ao exercício: ele contém uma cópia integral dos
dados, com as mesmas classificações de sensibilidade do original (`§24.13`).

## RPO e RTO — medidos, não estimados

| | Valor | De onde vem |
|---|---|---|
| **RTO** (tempo para voltar) | **~2 minutos** neste volume | 3 s de container + restore + 2 s de API |
| **RPO** (dado que se perde) | **até 24 h** | é o intervalo do backup automático do Neon Free |

⚠️ O RTO medido vale para **este volume** — 125 KB, 34 tabelas, dezenas de linhas. Ele
cresce com os dados, e não é uma promessa para uma base de produção com anos de folha.

## Limitações declaradas

- **O exercício usou a base de desenvolvimento como origem**, não o Neon de produção.
  Restaurar da produção exigiria baixar o backup do provedor — e o procedimento é o mesmo
  a partir do passo 2.
- **O Neon Free faz backup do provedor**, e a política de retenção dele não foi verificada
  neste exercício.
- **Não há automação**: o exercício é manual e precisa ser repetido de tempos em tempos.
  Backup testado uma vez é melhor que nunca testado, e pior que testado periodicamente.
- **A senha do container efêmero é literal neste documento por não proteger nada** — o
  container é criado e destruído no mesmo exercício, e não é alcançável de fora da máquina.
