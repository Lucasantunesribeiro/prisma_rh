# ADR 009 — Folha protegida por trava de linha, e não por token de versão

**Status:** aceita · **Data:** 2026-10-08 (correção pós-roadmap, item PR-01)

## Contexto

Uma folha fechada é fato histórico (`CLAUDE.md §4.3`): não aceita cálculo, lançamento nem
reabertura. Quem garantia isso era `FolhaPagamento.GarantirAberta` — que confere o estado
que a requisição **leu**.

Com duas requisições ao mesmo tempo, isso não basta:

```text
B lê a folha (aberta) ─┐
                       │   A lê, fecha, grava e confirma
B lança e grava ◀──────┘   (B ainda tem o retrato antigo na memória)
```

A folha continua marcada como fechada, mas com um lançamento a mais — e o total que a
auditoria do fechamento registrou deixa de ser o total gravado. Reproduzido contra
PostgreSQL real em `FolhaConcorrenciaTestes` **antes** da correção: 13 de 14 cenários
falhavam, incluindo dois fechamentos simultâneos gerando duas auditorias.

## Decisão

**`SELECT ... FOR UPDATE` na linha da folha, dentro de uma transação explícita, antes de
carregar o agregado.**

Toda escrita na folha e nos seus filhos passa por um único caminho
(`FolhasEndpoints.AlterarFolhaAsync`): abre a transação → trava a folha (`TravaFolha`) →
carrega → aplica o domínio → grava filhos, totais e auditoria → confirma. Quem chega
depois espera na fila do PostgreSQL; quando entra, a leitura já vê o fechamento
confirmado, e `GarantirAberta` recusa com a mensagem real.

Detalhes que fazem parte da decisão:

- a trava filtra pela **organização** — id de outro tenant não casa e não enfileira nada;
- `lock_timeout` de 5 s, só naquela transação: espera longa vira **409 "Folha em uso"**,
  e não requisição presa;
- **sem nova tentativa automática**: repetir sozinho um comando financeiro poderia
  duplicar lançamento;
- o commit existe num lugar só, então nenhum tipo de cálculo consegue responder 200 sem
  ter confirmado o que calculou.

## O que foi recusado

**Token de versão (concorrência otimista).** Uma coluna de versão conferida no `UPDATE` da
folha é a resposta clássica — e aqui ela tem um furo: **alteração que só mexe em filho
não faz `UPDATE` na folha**. Um lançamento informativo de valor zero não muda total
nenhum, o EF não emite `UPDATE` na raiz, e o token nunca é conferido. Fechar o furo exigiria
forçar a raiz como modificada em toda operação, e lembrar disso em cada caminho novo. Além
disso, exigiria migration em produção, e o conflito chegaria como erro genérico em vez da
recusa do domínio.

**Reaproveitar `VersaoCalculo` como token.** Ela tem significado de negócio — "quantas vezes
foi calculada" —, aparece na tela e na auditoria. Usá-la para controle de concorrência a
faria andar por motivo que não é cálculo.

**Trava em memória do processo** (`lock`, `SemaphoreSlim`). A API roda em Lambda: cada
container é um processo, e uma trava em memória não enxerga o vizinho.

**Bloquear o botão na tela.** Esconde o problema de quem clica duas vezes e não protege
contra duas pessoas, duas abas ou um script. O backend é a autoridade (`CLAUDE.md §24.4`).

**Advisory lock por folha.** Funcionaria, mas exige derivar uma chave numérica do `Guid`, e
a trava de linha já existe, já é do registro certo e já é liberada no commit.

## Consequências

**Boas:** a garantia vive no banco, e não depende de nenhum caminho de código "lembrar";
cobre filho e raiz igualmente; nenhuma migration; a mensagem de recusa é a do domínio;
auditoria e dado gravados juntos ou desfeitos juntos (provado com falha forçada depois do
`SQL` e antes do commit).

**Ruins:** escritas na **mesma** folha passam a ser serializadas — duas pessoas lançando no
mesmo holerite esperam uma pela outra. Para folha de pagamento, onde a escrita é rara e a
correção é o que importa, é o custo certo. Escritas em folhas diferentes não se esperam.

**Cuidado permanente:** rota nova que escreva na folha ou nos seus filhos **precisa** passar
por `AlterarFolhaAsync`. Escrever direto, com `SaveChangesAsync` solto, reabre a corrida —
e `FolhaConcorrenciaTestes` só protege os caminhos que conhece.
