# ADR 008 — A IA explica; ela não calcula

**Status:** aceita · **Data:** 2026-09 (Fase 11)

## Contexto

O produto ganhou uma camada de IA. Havia duas formas de usá-la: deixá-la **produzir**
valores e conclusões, ou deixá-la **explicar** o que o código determinístico já produziu.

## Decisão

**Explicar.** O critério está escrito e é operacional:

> Se o valor entra numa conta, num holerite ou numa obrigação, ele veio do C#. Se é frase
> explicando um valor que o C# já produziu, pode ter vindo da IA — e precisa estar
> rotulada como tal na interface.

Três consequências de desenho, cada uma verificável:

1. **O resumo executivo devolve os números apurados por consulta, sempre** — inclusive
   quando o provedor está fora do ar. A tela mostra os números **ao lado** da prosa, e não
   dentro dela: se o modelo escrever "sete inconsistências" onde há seis, a divergência
   fica visível na mesma tela.
2. **A consulta em linguagem natural não gera SQL.** O modelo propõe `campo`, `operador` e
   `valor`; o `VocabularioConsulta` confere os três contra listas fechadas; o C# monta o
   `Where` tipado. O que estiver fora é recusado **e mostrado**, nunca ignorado em silêncio.
3. **A camada é de leitura.** Nenhum caminho iniciado por resposta de modelo termina em
   escrita no banco. A única escrita das rotas de IA é o evento de auditoria, e ele
   acontece independentemente do que o modelo respondeu.

## O que foi recusado

**IA calculando ou conferindo valor de folha.** Um modelo de linguagem não é determinístico
nem auditável, e folha precisa das duas coisas — às vezes anos depois.

**Agente que executa ações.** Ele precisaria de permissão de escrita, e nenhuma defesa de
prompt sobrevive à combinação "entrada em linguagem natural" com "capacidade de agir".

**Enviar nome, CPF e matrícula ao provedor.** A explicação de *"desligado em 20/07 e mesmo
assim tem holerite"* não fica pior sem o nome, e mandá-lo transformaria cada chamada numa
transferência de dado pessoal identificável para fora. Há teste inspecionando o corpo HTTP.

**RAG e banco vetorial.** Nenhuma pergunta do produto exige busca semântica sobre corpus;
seria infraestrutura procurando problema.

## Consequências

**Boas:** o produto funciona igual com a IA desligada — sem chave configurada, os botões
nem aparecem. O isolamento multiempresa não depende do modelo se comportar, porque a
consulta que ele propõe roda sob o filtro global (ADR 002).

**Ruins:** a IA ajuda menos do que poderia. Ela não responde "quanto deveria ter sido pago"
— responde "o que esta divergência significa e o que conferir primeiro".

**Limite honesto:** a interpretação pode ser mais estreita que a pergunta. Na verificação
ao vivo, *"ainda estão abertas"* virou `Status = Detectada`, quando `Status ≠ Resolvida`
seria mais fiel. É por isso que a tela mostra em que a pergunta virou **antes** dos
resultados.
