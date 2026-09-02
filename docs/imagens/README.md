# Imagens do portfólio

Esta pasta guarda as capturas de tela referenciadas no `README.md`.

> ✅ **Capturadas em 02/09/2026**, contra a produção, entrando pela conta pública da
> demonstração (`visualizador@prisma.exemplo`, somente leitura). O que antes impedia era a
> ausência de um caminho de entrada sem digitar senha; o botão *"Entrar na demonstração"*
> resolveu isso.
>
> ⚠️ **São `.jpg`, e não `.png`.** A ferramenta de captura entrega JPEG; reconverter para
> PNG não recupera qualidade nenhuma e **quadruplicou** o peso (520 KB → 2,4 MB). Extensão
> tem de dizer a verdade sobre o conteúdo.
>
> ⚠️ **1568×705 a 751 px, e não 1440×900.** A janela foi ajustada para 1440 de largura — é
> o que define a densidade do layout, e é o que a regra abaixo realmente quer —, mas a
> ferramenta normaliza a imagem exportada. O layout capturado é o largo, que é o ponto.

## O que capturar

Ordem escolhida para contar a história do produto em seis imagens — de "o que é" até "por
que é confiável".

| # | Arquivo | Tela | Por que esta |
|---|---|---|---|
| 1 | `01-painel.jpg` | `/painel` | Primeira impressão: indicadores, pendências por responsável, evolução por competência |
| 2 | `02-folha-detalhe.jpg` | `/folhas/{id}`, com um holerite aberto | O centro do produto: totais, bases de INSS/FGTS/IRRF e lançamentos |
| 3 | `03-memoria-calculo.jpg` | O mesmo holerite, memória expandida | **A imagem mais importante.** É o diferencial: o número **e a conta que levou até ele** |
| 4 | `04-inconsistencias.jpg` | `/inconsistencias`, com a gaveta aberta | Workflow, justificativa e linha do tempo — os cinco estados visíveis na lista atrás |
| 5 | `05-pergunta-em-portugues.jpg` | `/inconsistencias`, após uma consulta | Mostrando **"Entendi como: Severidade = Alta e Status ≠ Resolvida"** — a prova de que o modelo propõe e a aplicação decide |
| 6 | `06-auditoria.jpg` | `/auditoria` | Trilha somente-inserção: quem, o quê, quando, de quanto para quanto |

## Regras para as capturas

Estas não são preferências estéticas — são o Security Gate da Fase 13, item 2.

- **Só o ambiente de demonstração**, com a base fictícia. Nenhum CPF, salário ou nome real.
- **Nenhuma URL interna, nome de bucket, ARN, id de conta AWS ou string de conexão** —
  nem no conteúdo, nem na barra de endereço, nem numa aba vizinha.
- **Nenhum terminal com credencial**, nem truncada.
- **Nenhuma aba do navegador** com outro sistema aberto.
- Janela em ~1440×900. Mais largo espreme a densidade; mais estreito ativa o layout
  compacto e a tela parece outra coisa.
- Tema claro, para legibilidade em README no GitHub.

> Documentar **arquitetura** de segurança em portfólio é positivo e esperado. Publicar a
> **configuração exata** que a implementa, não.

## Depois de capturar

Referencie no `README.md` logo abaixo da seção *"O que ele faz"*, no formato:

```markdown
| ![Memória de cálculo](docs/imagens/03-memoria-calculo.jpg) |
|:--:|
| *Cada valor guarda os passos que o produziram — e qual parâmetro estava vigente.* |
```

A legenda importa tanto quanto a imagem: quem passa os olhos lê a legenda e não a tela.

## O que a captura mostrou que o código não mostrava

Fotografar a tela encontrou defeitos que nenhum teste tinha pego, porque nenhum teste
**olha**:

| Achado | Onde estava |
|---|---|
| Texto de interface sem acento — *"Liquido negativo"*, *"Rubrica lancada em duplicidade"*, *"Base de contribuicao"* | 23 strings nas regras de análise e nas calculadoras |
| Nomes de rubrica sem acento — *"Salario base"*, *"Comissao"* | semeadura **e** banco de demonstração |
| `ExplicacaoIa` cru na coluna *Sobre* da auditoria | faltava o rótulo no mapa do frontend |
| Descrições da trilha sem acento — *"atribuida"*, *"recebeu uma evidencia"* | `InconsistenciasEndpoints` |

⚠️ **E um que era meu, não do produto:** as justificativas enviadas por `curl` a partir do
Git Bash chegaram em Latin-1 e ficaram gravadas como `Promo��o a Analista
S�nior`. O produto estava certo; o cliente é que mandou errado. Refeito por um cliente
que serializa em UTF-8 de verdade.
