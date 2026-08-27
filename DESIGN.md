# DESIGN.md — Prisma RH

> **Fonte de verdade visual do Prisma RH.**
>
> Toda tela existente e toda tela futura seguem este documento. Ele descreve a
> implementação que está no repositório — não uma intenção.
>
> Escrito em 27/08/2026, a partir do redesenho completo do frontend.

---

## 1. Princípio

**Sofisticação silenciosa, com presença.** A beleza vem de proporção,
hierarquia, tipografia, alinhamento e densidade — nunca de efeito visual.

Mas silencioso **não é apagado**. Uma interface inteiramente branca e cinza lê
como rascunho, não como produto. O azul da marca aparece onde estrutura a
leitura: sidebar em superfície azulada, marca em azul, item ativo preenchido,
ação primária sólida, tags de estado coloridas. O que não se faz é pintar
metade da tela.

O critério prático, aplicado a cada tela:

> Se eu apagar a marca Prisma RH, esta página parece um template chamado
> "Modern Admin Dashboard"?

Se sim, ela está errada. Três perguntas de apoio:

1. A tela foi organizada em torno de uma **tarefa real de folha**?
2. Existe algum bloco cuja única função é **ocupar espaço**?
3. Existe algum ícone **sem função**?

O usuário precisa trabalhar aqui oito horas. Nada na tela pode cansar.

---

## 2. Referências estudadas

| Produto | O que foi extraído | O que foi descartado |
|---|---|---|
| **Personio / Rippling** | O ciclo da folha como objeto central; revisão antes de processar; comparação entre estados | Métricas de "conformidade" e pendências que o Prisma RH não calcula |
| **Stripe Dashboard** | Arquitetura de informação previsível; densidade sem aperto; detalhe em painel lateral | Busca global (não existe backend para ela) |
| **Carbon Design System** | Data Table como componente de primeira classe: toolbar, estados, ações por linha, semântica de tabela | A biblioteca em si — Carbon é referência de UX, não dependência |
| **Linear** | Economia de cor; tipografia pequena e firme; interações sem animação decorativa | Tema escuro e a estética de produto de engenharia |

**Nada foi copiado visualmente.** O que se extraiu foram princípios.

### A referência do Stitch

O Stitch é a **base visual desta interface**. Adotado:

- **rótulos de seção em caixa alta com tracking** — viraram `.rotulo-secao`, a
  assinatura tipográfica do produto;
- **faixa tipográfica** de resumo financeiro, com divisores verticais;
- **sidebar em superfície azulada**, com a marca em azul e item ativo
  preenchido;
- **status tags em caixa alta**, coloridas por semântica;
- contexto de empresa logo abaixo da marca;
- densidade e altura de linha das tabelas.

Descartado, com motivo:

- **`max-width: 1200px` no conteúdo** — desperdiça uma coluna inteira de tabela
  em telas de 1440px;
- **navegação para módulos inexistentes** (Conferência, Integrações, Auditoria)
  — link sem destino ensina a desconfiar da navegação;
- **dados inventados** (542 funcionários, 96,8% de conformidade, pendências) —
  nada disso existe no sistema, e inventar número em tela de folha é o pior
  tipo de mentira que uma interface pode contar;
- **tokens Material 3** (`surface-container-*`) e `Material Symbols`;
- **sino de notificação e ícone de ajuda** sem funcionalidade por trás.

O tom dos tons foi reequilibrado: as tags do Stitch usavam contraste baixo
demais para AA, e o azul da marca foi escurecido de `#164483` para `#1f4b8f`
para funcionar também como **cor de texto**, não só de fundo.

---

## 3. Paleta

Neutral-first: a maior parte da tela é neutra. O accent aparece na ação
principal, no item selecionado e no foco — **nunca em metade da interface**.

Tokens em `frontend/src/index.css`. Valor arbitrário (`text-[#...]`,
`bg-[#...]`) espalhado por página é **proibido**: se uma cor precisa existir,
ela vira token.

| Papel | Token | Valor |
|---|---|---|
| Fundo do workspace | `--background` | `#f6f7fb` |
| Superfície (card, tabela) | `--card` | `#ffffff` |
| **Sidebar** | `--sidebar` | `#e9edf7` |
| **Item ativo da sidebar** | `--sidebar-accent` | `#cbd9f2` |
| Texto principal | `--foreground` | `#111a2e` |
| Texto secundário | `--muted-foreground` | `#5b6779` |
| Borda | `--border` | `#e2e6ee` |
| Borda estrutural | `--border-forte` | `#cbd3e1` |
| **Accent (marca)** | `--primary` | `#1f4b8f` |

O azul tem 8,2:1 sobre branco. Ele é usado como **cor de texto da marca**, não
só como fundo de botão.

### Semânticas

Texto e fundo andam em par, para garantir contraste AA. **Sem grandes
superfícies semânticas** — só texto, ponto e badge.

| Estado | Texto | Fundo |
|---|---|---|
| Sucesso | `#05683f` | `#dcf5e8` |
| Atenção | `#9a4b06` | `#fdf0d5` |
| Crítico | `#a41d13` | `#fde5e3` |
| Informação | `#14509e` | `#e2ecfb` |
| Neutro | `#414d61` | `#e9edf4` |

---

## 4. Tipografia

**Geist**, fonte única. Nenhuma segunda família.

| Uso | Tamanho | Peso |
|---|---|---|
| Título de página | 26px | 600, tracking −0.02em |
| Valor da faixa financeira | 19px | 600–700, tabular |
| Corpo / tabela | 13,5px | 400 |
| Marca | 15px | 700, em azul |
| **Rótulo de seção** (`.rotulo-secao`) | 11px | 700, **caixa alta**, tracking 0.08em |
| Status badge | 10,5px | 700, caixa alta |

### A assinatura tipográfica

`.rotulo-secao` — caixa alta, pequena, com tracking — é o que separa blocos
sem gastar o peso de um título. Ela aparece nos grupos da sidebar
(`PESSOAS`, `FOLHA`), nos cabeçalhos de tabela (`COMPETÊNCIA`, `LÍQUIDO`), nos
rótulos da faixa financeira e nos títulos de seção.

É essa repetição que faz a tela parecer organizada por alguém, em vez de
empilhada. Caixa alta **só aqui** — nunca em título de página, botão ou corpo.

**Valores financeiros e qualquer coluna numérica usam `.tabular`**
(`font-variant-numeric: tabular-nums`). Sem largura fixa de algarismo,
`1.111,11` e `8.888,88` desalinham na vertical e a coluna deixa de poder ser
conferida de relance — que é a única razão de ela existir.

Sem monoespaçada para dinheiro.

---

## 5. Grid e espaçamento

Base **4px**. Padding do workspace: **24px**.

| Elemento | Medida | Token |
|---|---|---|
| Sidebar expandida | 224px | `--largura-sidebar` |
| Sidebar recolhida | 60px | `--largura-sidebar-recolhida` |
| Topbar | 52px | `--altura-topbar` |

**Não existe `max-width` global.** O workspace ocupa a largura restante.
Páginas de leitura ou formulário limitam a própria largura, individualmente
(Status usa `max-w-2xl`; formulários em drawer, 448–672px).

Prioridade de teste: 1440×900 → 1366×768 → 1280px.

---

## 6. Application shell

**Um único shell**, em `frontend/src/layout/ApplicationShell.tsx`. Toda página
autenticada renderiza dentro dele.

```
┌──────────────┬────────────────────────────────────┐
│ marca azul   │ breadcrumb          competência    │  topbar 52px
│──────────────├────────────────────────────────────┤
│ empresa   ⌄  │                                    │
│──────────────│  workspace                         │
│ navegação    │  (largura total, padding 24px)     │
│              │                                    │
│──────────────│                                    │
│ usuário  ⌄   │                                    │
│ recolher     │                                    │
└──────────────┴────────────────────────────────────┘
```

**Shell por módulo é proibido.** Foi o que produziu telas com arquiteturas
visuais diferentes.

### Sidebar

**Superfície azulada, não branca nem navy.** É o que dá identidade ao chrome
sem recorrer a um bloco escuro gigante — e é o que separa "produto" de
"rascunho".

- **Topo**: símbolo sólido de 28px + "Prisma RH" **em azul, 15px bold**. Sem
  logotipo grande, sem card de marca.
- **Contexto de empresa**: nome fantasia + CNPJ. Vira seletor com busca **apenas
  quando há mais de uma empresa** — seletor com uma opção é clique que nunca
  muda nada.
- **Navegação**: agrupada (Pessoas · Folha · Administração), derivada das rotas
  que **existem**.
- **Rodapé**: usuário com iniciais, perfil, e "Sair" em menu. Sem botão gigante
  de logout.
- **Recolhível**: ícones + tooltip, preferência em `localStorage` dentro de
  `try/catch`.

O contexto de empresa é **preferência de interface**, não conceito de domínio:
o backend continua derivando a organização do token.

### Topbar

Breadcrumb à esquerda, competência à direita quando fizer sentido.

**Sem busca global e sem sino de notificação** — nenhum dos dois existe no
sistema. Ícone que não faz nada sugere função inexistente.

Páginas publicam breadcrumb e competência pelo hook `usePagina`.

---

## 7. Page header

Um padrão para todas as páginas: título, descrição e **no máximo uma ação
principal**. Duas ações primárias lado a lado não têm hierarquia.

Ações secundárias: `ghost`, `outline` ou menu.

---

## 8. Tabelas

O componente mais importante do produto: `components/sistema/DataTable.tsx`.

- **Não fica dentro de card apertado** — ocupa o workspace com borda discreta;
- linha de ~40px; cabeçalho 12px em `muted`;
- **colunas numéricas alinhadas à direita e tabulares**;
- coluna secundária pode ser escondida abaixo de 1280px (`secundaria: true`) —
  melhor ocultar do que espremer até ficar ilegível;
- scroll horizontal **dentro** do container, nunca na página;
- linha clicável leva ao detalhe;
- `<caption class="sr-only">` para leitor de tela.

### Toolbar

Padrão único: busca à esquerda, filtros em seguida, ação contextual à direita.
Nunca uma barra diferente por tela.

### Estados

Componentes únicos em `components/sistema/Estados.tsx`: carregando (esqueleto de
tabela), vazio, sem resultado, sem permissão, erro.

Mensagem sempre **específica**: "Não foi possível carregar os funcionários",
nunca "Ops, algo deu errado".

---

## 9. Badges

Apenas para **estado**: `ATIVO`, `FECHADA`, `CALCULADA`. Nunca para valor
qualquer.

Pequenos, em caixa alta, com fundo semântico suave e **ponto colorido** — o
ponto existe para não depender só de cor. Cada tom tem par texto/fundo com
contraste AA.

Caixa alta aqui é deliberada: é o único lugar, junto de `.rotulo-secao`, onde
ela aparece — e é o que dá a eles identidade de *tag* sem precisar de borda.

---

## 10. Formulários

Ficam em **drawer**, acionados pela ação principal. Formulário permanentemente
aberto acima de uma lista ocupa metade da tela para uma ação pouco frequente e
empurra a lista para baixo da dobra.

- largura confortável: 448–672px;
- agrupados por seção quando houver mais de um assunto;
- label sempre visível;
- mensagem de validação junto do campo;
- ações no rodapé, com Cancelar à esquerda da ação primária.

---

## 11. Drawers e dialogs

**Drawer** para detalhe que não merece rota: holerite, memória de cálculo,
estabelecimentos, formulários. Podem empilhar (holerite → memória).

**Dialog** apenas para ação com consequência real: fechar folha, inativar
rubrica. Modal por clique treina o usuário a confirmar sem ler.

Os dois usam `Dialog` do Radix — foco preso, Escape, retorno de foco e
`aria-modal` corretos de fábrica.

---

## 12. Domínio bem representado

Três telas carregam a identidade do produto:

**Detalhe da folha** — resumo financeiro em **faixa tipográfica**: rótulo em
caixa alta pequena, valor em 19px tabular logo abaixo, divisores verticais
discretos, delimitada por borda em cima e embaixo. Nunca grade de KPI cards —
quatro números alinhados dizem o mesmo que quatro caixas com sombra, em um
terço do espaço.

```
FUNCIONÁRIOS  │  PROVENTOS      │  DESCONTOS      │  LÍQUIDO
3             │  R$ 10.100,00   │  − R$ 600,00    │  R$ 9.500,00
```

**Holerite** — tratado como documento financeiro: código, rubrica, referência,
proventos e descontos em colunas alinhadas, totais no rodapé, líquido com peso
tipográfico maior. Sem faixa azul.

**Memória de cálculo** — em drawer, ao clicar na rubrica calculada. Nunca uma
parede permanente ao lado do holerite. Para faixas de INSS: **tabela**
(etapa · conta · valor), nunca card por faixa nem timeline decorativa.

**Histórico contratual** — linha do tempo que mostra o que **mudou**
(`5.100 → 6.200`, riscado + seta), não só o estado. É a característica central
do Prisma RH: alteração não sobrescreve o passado.

**Incidências** — texto (`INSS · FGTS · IRRF`), não três pastilhas coloridas por
linha. Em vinte rubricas, sessenta pastilhas viram ruído.

---

## 13. Acessibilidade

Meta prática: **WCAG AA**.

- foco visível único, definido uma vez em `:focus-visible`;
- todo botão icon-only tem `aria-label`;
- tabelas semânticas com `<th scope>` e `<caption>`;
- estados anunciados com `aria-live` / `role="status"` / `role="alert"`;
- nunca depender só de cor — status tem ponto **e** texto;
- `prefers-reduced-motion` respeitado globalmente.

---

## 14. Permissões

A interface respeita os cinco perfis. Ação que o usuário não pode executar não
é oferecida.

**O backend continua sendo a autoridade.** Esconder botão é conforto visual;
mostrar um botão que sempre devolve 403 é pior.

---

## 15. Microcopy

100% português do Brasil, curto e profissional.

Proibido: "Oops", "Awesome", "Magic", "Powered by AI", linguagem promocional
dentro do produto.

---

## 16. PROIBIDO

Lista fechada. Vale para toda tela futura.

- sidebar escura e superdimensionada;
- gradiente, glassmorphism, glow, sombra pesada;
- **grade de KPI cards**;
- Material Symbols ou segundo design system;
- card cuja única função é ocupar espaço;
- ícone sem função;
- emoji, ilustração de SaaS, avatar de IA, sparkle;
- IA como elemento promocional;
- **página com shell próprio**;
- **`max-width` global apertando tabela**;
- estilos diferentes para o mesmo componente;
- link de navegação sem destino;
- funcionalidade futura na sidebar antes de existir;
- **mock substituindo dado que a API já fornece**;
- radius grande;
- caixa alta fora de `.rotulo-secao` e status badge;
- interface inteiramente branca e cinza, sem presença da marca.

---

## 17. Tecnologia

React · TypeScript · Vite · Tailwind v4 · shadcn/ui · Radix · **Lucide** para
ícones.

**Nenhuma dependência nova foi adicionada neste redesenho.** Os primitivos que
faltavam (dropdown, drawer, dialog, tooltip) foram construídos sobre o
`radix-ui` já instalado.

Proibido adicionar: Material UI, Ant Design, Chakra, Carbon, biblioteca de
animação, novo gerenciador de estado, Storybook.
