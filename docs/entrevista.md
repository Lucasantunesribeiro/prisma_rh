# Material para entrevista

As perguntas que este projeto convida, com a resposta curta e **o arquivo onde ela está no
código**. Se a resposta não puder ser mostrada, ela não vale.

Regra de uso: leia a resposta, abra o arquivo, e confira. Decorar isto sem abrir o código
produz exatamente a impressão que se quer evitar.

---

## Segurança

### Por que o access token não toca o `localStorage`?

Porque `localStorage` é legível por qualquer script da página. Um XSS levaria a sessão
inteira. O access token vive em **memória do JavaScript** e vai no header `Authorization` —
que o navegador **não** envia sozinho, e é por isso que os endpoints que dependem dele não
são vulneráveis a CSRF.

📁 `frontend/src/api/cliente.ts` · [ADR 004](adr/004-token-em-memoria-refresh-em-cookie.md)

### Por que o refresh é guardado como hash, e não em texto puro?

Porque vazamento do banco não pode entregar sessões ativas. Guardado como hash, o que
vazou não serve para nada — a forma bruta só existe no cookie do usuário.

E ele é **opaco**, não JWT: sendo uma linha no banco, revogar é `UPDATE`. Um JWT seria
válido até expirar, e revogá-lo exigiria uma lista de bloqueio — ou seja, estado no
servidor de qualquer jeito.

📁 `backend/src/PrismaRH.Infraestrutura/Identidade/` · **rotação a cada renovação**, e
**reúso derruba todas as sessões** daquele usuário, porque reúso é sinal de roubo.

### Por que recurso de outro tenant devolve 404 e não 403?

Porque um 403 diz *"este id existe e você não pode vê-lo"*. Isso é um **oráculo**: permite
enumerar identificadores e mapear o vizinho um id por vez, sem ler um único dado.

📁 [ADR 003](adr/003-404-em-vez-de-403.md) · teste:
`backend/testes/PrismaRH.Testes/Seguranca/VarreduraIdorTestes.cs`

### Por que o isolamento é filtro global e não `where` em cada consulta?

Porque `where` manual funciona até a consulta número setenta, escrita numa sexta-feira. A
segurança passaria a depender de ninguém esquecer.

O filtro global inverte isso: consulta nova nasce segura, e **atravessar** exige
`IgnoreQueryFilters()` explícito, que é visível em revisão.

⚠️ A parte que importa saber: **ele protege consultas dentro de uma requisição HTTP e mais
nada.** Não alcança job assíncrono (que não tem requisição), fila, cache, log nem arquivo.
Cada um tem defesa própria.

📁 `backend/src/PrismaRH.Infraestrutura/Persistencia/PrismaRhDbContext.cs` ·
[ADR 002](adr/002-isolamento-por-filtro-global.md)

### Por que o login confere um hash falso quando o usuário não existe?

Porque sem isso o **tempo de resposta** enumera usuários. E-mail inexistente responderia
rápido (sem hash), e-mail existente responderia devagar (com hash) — a diferença é
mensurável e é informação. A resposta é `CredencialInvalida` nos três casos: e-mail
malformado, inexistente e senha errada.

É defesa contra canal lateral de temporização, irmã da decisão do 404.

### O que é um Security Gate, e por que ele não é a fase de Hardening?

Um Security Gate é um conjunto de onze perguntas respondidas **por escrito ao fim de cada
fase** — ameaças introduzidas, controles, testes, multiempresa, exposição, permissões,
logging, dependências, secrets, superfície pública e custo/abuso.

Ele não é o Hardening porque **segurança não é uma fase**. Se fosse, toda fase anterior
seria escrita sabendo que "depois alguém arruma".

O Hardening continua existindo, e faz o que um gate individual não alcança: **falhas de
composição** (cada fase correta, a junção errada), **desvio acumulado** e o **olhar
adversarial**. Foi ele que achou os três defeitos da Fase 12.

📁 `ROADMAP.md §4.1` e o gate executado de cada fase

---

## Backend

### Como a autorização é aplicada, e o que acontece se alguém esquecer?

Política nomeada por rota, aplicada no backend. Esconder botão no frontend não é mecanismo
de segurança.

Se alguém esquecer, **a rota devolve 401** — há uma `FallbackPolicy` exigindo usuário
autenticado. Isso foi acrescentado na Fase 12, quando a auditoria descobriu que sem ela a
rota esquecida nasceria **anônima**.

O fallback é a rede, não o piso: ele só exige *alguém logado*, não o **perfil certo**. Por
isso `InventarioDeRotasTestes` continua exigindo política explícita em toda rota de negócio.

📁 `backend/src/PrismaRH.Api/Identidade/PoliticasAutorizacao.cs`

### Como você prova que a matriz de permissões está certa?

Avaliando as cinco políticas contra os cinco perfis pelo `IAuthorizationService` **real** e
comparando com a matriz declarada — não lendo documento. Se código e documento divergirem,
o código é o fato.

📁 `backend/testes/PrismaRH.Testes/Seguranca/MatrizDeAutorizacaoTestes.cs`

### Onde há transação, e por que?

Onde a operação precisa ser atômica — calcular folha, aplicar importação, alterar valor
base do FGTS junto com o evento de auditoria. O evento e a alteração vão na **mesma**
transação: auditoria que pode faltar quando a gravação dá errado não é auditoria.

### Como o sistema lida com concorrência?

Três mecanismos, cada um para um caso:

| Caso | Mecanismo |
|---|---|
| Vigências sobrepostas | *Exclusion constraint* no banco — a corrida é impossível, não improvável |
| Orçamento global de blobs | `pg_advisory_xact_lock` — escopo de transação, funciona atrás do PgBouncer |
| Confirmações simultâneas | `ConflitoDeBanco` traduz `40P01`, `40001` e `23505` para **409**, e não 500 |

O terceiro veio de um teste intermitente que **era defeito real**: sob carga, o PostgreSQL
aborta transações concorrentes e a exceção chegava crua.

📁 `backend/src/PrismaRH.Api/Producao/ConflitoDeBanco.cs`

### Por que não há Repository genérico, MediatR ou AutoMapper?

Porque o `DbContext` já é repositório e unit of work; porque não há pipeline de comandos
que justifique um mediador; e porque os DTOs são poucos e explícitos. Cada um seria uma
indireção que o leitor atravessa sem receber nada.

---

## Domínio

### O que é competência, e por que ela tem tipo próprio?

O mês de referência da folha. É `readonly record struct` com ano e mês, convertido para o
inteiro `202608` no banco — uma coluna só, ordenável e comparável.

Se fosse `string` solta, `"2026-8"` e `"2026-08"` seriam competências diferentes, e
ordenação alfabética colocaria `2026-10` antes de `2026-9`.

📁 `backend/src/PrismaRH.Dominio/Folha/Competencia.cs`

### O que é memória de cálculo, e por que ela é persistida?

As linhas que mostram **como** um valor foi obtido. Ela é gravada — e não recalculada sob
demanda — porque reconstruir a conta depois exigiria os parâmetros vigentes **daquele dia**,
e parâmetros mudam.

Nem tudo é gravado: as **bases de cálculo** são derivadas, porque são soma simples de
lançamentos que já estão na mesma tela, cada um com a incidência congelada. Gravar
duplicaria dado. O critério é: *a explicação é reconstruível a partir do que está
persistido?*

📁 `backend/src/PrismaRH.Dominio/Folha/LinhaMemoriaCalculo.cs`

### Por que o lançamento congela código, nome, tipo e incidências da rubrica?

Porque uma folha fechada é um **fato histórico**. Se a lei mudar e o vale-transporte passar
a compor a base de FGTS, a folha de agosto tem que continuar dizendo qual base ela usou.

Efeito colateral bom: alterar a incidência de uma rubrica passa a ser barato — só afeta
cálculos futuros.

📁 `backend/src/PrismaRH.Dominio/Folha/LancamentoFolha.cs`

### Como uma regra de análise é versionada?

A regra é **código**, e sua `Versao` sobe quando a lógica muda. O número é **congelado em
cada resultado**: um achado de agosto continua dizendo com qual régua foi produzido, mesmo
depois de a régua mudar.

Não há tabela `VersaoRegra` — ela guardaria uma cópia de um número que já vive no código,
junto da lógica que ele versiona, e a cópia é a que envelhece.

E **afrouxar a tolerância** é auditado, porque é o jeito mais barato de fazer uma
divergência sumir do relatório de conferência.

📁 `backend/src/PrismaRH.Dominio/Analises/`

### Qual regra legal você não implementou, e por quê?

O pagamento do 13º com incidência **na primeira parcela**. Há **contradição entre fontes
oficiais**: uma nota do eSocial/FGTS Digital diz que INSS e IRRF incidem apenas na apuração
anual, e a página *"Como pagar a primeira parcela"* manda descontá-los do adiantamento.

A segunda é provavelmente do regime doméstico, mas a página não deixa isso explícito.
Concluir por eliminação seria interpretação, não implementação — então **três dos oito
motivos de rescisão e essa incidência ficam bloqueados, e o sistema diz isso** em vez de
chutar.

Esta é a resposta que mais diferencia numa entrevista: mostra que o critério é *fonte
oficial*, não *entregar mais*.

---

## AWS e assíncrono

### Por que o processamento assíncrono só apareceu na Fase 9?

Porque antes disso não havia caso real. Importação síncrona resolvia o volume que existia.
Fila adicionada antes do problema é complexidade procurando justificativa.

### Como o job garante isolamento se não há requisição?

A mensagem carrega `IdOrganizacao` **explícito**, e o worker abre o contexto a partir dela
— depois **confere contra o objeto processado**. Sem requisição não há filtro global, então
o isolamento tem que ser reconstruído deliberadamente.

📁 `backend/src/PrismaRH.Dominio/Assincrono/MensagemTrabalho.cs` — duas barreiras: `Ler()`
valida o esquema, `Conferir()` valida o tenant.

### Idempotência, retry e DLQ: como funcionam aqui?

Retentativa com teto; ao esgotar, `FalharDefinitivamente`. Isso veio de um defeito real: o
método `Falhar` devolvia o trabalho à fila enquanto houvesse tentativas, e com a mensagem
já descartada o job ficava **eternamente pendente**.

DLQ com retenção definida. Correlação em cada mensagem, ligando trilha de negócio e log
técnico.

### Como o custo foi mantido em US$ 0,00?

Por **exclusão**, não por otimização: serviço que cobra por existir não entra. Sem S3, API
Gateway, NAT, KMS próprio, RDS ou provisioned concurrency.

E com **limites técnicos** para tornar a ultrapassagem improvável, porque Free Tier não é
teto de gasto — passar da franquia não bloqueia nada, apenas cobra.

📁 `backend/src/PrismaRH.Dominio/Importacao/OrcamentoSemCusto.cs` — cada número com a conta
escrita ao lado.

---

## IA

### Por que a IA explica e não calcula?

Porque folha precisa ser determinística e auditável, às vezes anos depois. O critério é
operacional: **se o valor entra numa conta, num holerite ou numa obrigação, ele veio do
C#.**

📁 [ADR 008](adr/008-ia-explica-e-nao-calcula.md)

### Como a intenção vira filtro estruturado sem gerar SQL?

O modelo devolve uma lista de tuplas de **texto** — `campo`, `operador`, `valor`. O
`VocabularioConsulta` confere as três contra listas fechadas, **por campo**: `Severidade >
Alta` é recusado, porque enum tem igualdade e não ordem de negócio.

O que passa vira `Where` tipado montado em C#. O que não passa **aparece na tela** — nunca
é ignorado, porque ignorar devolveria a lista inteira para quem pediu um recorte.

📁 `backend/src/PrismaRH.Infraestrutura/Ia/VocabularioConsulta.cs`

### Por que o isolamento não depende do comportamento do modelo?

Porque a consulta que ele propõe roda sobre `db.ResultadosAnalise`, que **já nasce sob o
filtro global**. `IdOrganizacao` nem está no vocabulário — mas mesmo que estivesse, não
mudaria nada.

Prova: `AConsultaGeradaPorIaNaoAtravessaAFronteiraDaOrganizacao` — a vizinha faz a mesma
pergunta, o modelo propõe o mesmo filtro, e ela não vê um único achado da outra organização.

### O que sai do sistema e o que fica?

**Sai:** nome da regra, categoria, severidade, a descrição que o motor gerou, e os valores.
**Não sai:** nome, CPF, matrícula, data de nascimento, endereço.

O prompt é montado **campo a campo**, nunca serializando a entidade — assim um campo novo
com dado pessoal não passa a vazar sem ninguém decidir isso. Há teste inspecionando o corpo
HTTP, e ele foi validado por *mutation testing*: acrescentar o nome ao prompt faz o teste
falhar.

### Como o custo por token foi limitado?

Modelo mais barato da família, 300 tokens de saída, 4.000 caracteres de entrada, 12 s de
prazo, cache de 24 h, 20 chamadas por hora **por organização**, e geração só sob clique.

📁 `backend/src/PrismaRH.Infraestrutura/Ia/OrcamentoIa.cs`

---

## A pergunta que separa

### O que deu errado neste projeto?

Vale ter três na ponta da língua, porque é a pergunta que mais revela:

**1. Um modelo aposentado que a suíte não pegava.** `gemini-2.5-flash-lite` aparecia
normalmente na listagem de modelos, e todos os testes com dublê passavam. A chamada real
devolvia `404 — no longer available to new users`. Lição: **suíte verde com dublê não prova
que o parceiro existe**, e desde então a fase tem verificação contra o provedor de verdade.

**2. Um teste "intermitente" que era defeito.** Sob carga, uma operação simultânea
devolvia 500 em vez de 409. A causa: `catch (DbUpdateException)` estreito demais — o
PostgreSQL aborta transações concorrentes com `40P01`/`40001` e a exceção chegava crua.
Lição: intermitência é hipótese sobre o teste, não conclusão.

**3. Uma conclusão minha errada, registrada no ROADMAP.** Na auditoria da Fase 12 apontei
um índice faltando que **já existia** — eu tinha lido uma listagem truncada. Ficou escrito
porque relatório que só mostra acerto não é auditoria.
