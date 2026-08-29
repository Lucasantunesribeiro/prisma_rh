# Prisma RH

Plataforma B2B de gestão, cálculo, conferência e auditoria de folha de pagamento brasileira.

> **Estado atual: FASE 4 CONCLUÍDA — 4A a 4G. Os cinco tipos de folha calculam.**
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
> Existe também **férias**, completas: quantos períodos cada contrato acumulou, quais já
> passaram do prazo, a **programação** dos dias — com fracionamento e abono validados pela
> CLT — e o **pagamento**, numa folha de tipo próprio, com o 1/3 constitucional e as
> incidências de cada verba conforme o Manual do eSocial.
> Do **13º salário** existem os **avos** — quantos meses de cada ano contam, e por quê.
> O desligamento exige o **motivo**, e a **rescisão é simulada**: saldo de salário, aviso
> prévio, férias vencidas e proporcionais com 1/3, e a multa do FGTS — cada regra com a
> norma que a sustenta. **Cinco motivos calculam; três ficam bloqueados** por falta de
> fonte oficial, e o sistema diz isso em vez de chutar. O pagamento do 13º e a folha de
> rescisão ainda não existem.
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
> escopo da subfase. O FGTS sobre **férias** veio na Fase 4E e a **multa rescisória** na
> 4G; o FGTS sobre o **13º** veio na 4F, e incide na competência de cada parcela.

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

**Rescisão (Fase 4G)**

Fontes, uma por regra: **Lei 12.506/2011** (aviso proporcional), **TST SDI-1
E-RR-1964-73.2013.5.09.0009** (a proporcionalidade só se exige da empresa), **CLT art. 146
§ único** e **Súmula 171 do TST** (férias proporcionais), **Lei 8.036/1990 art. 18** (multa
de 40% e 20%), **CLT art. 484-A** (acordo: metade) e o **Manual do FGTS Digital**.

| Motivo | Aviso | Metade | Férias prop. | Multa |
|---|---|:--:|:--:|:--:|
| Dispensa sem justa causa | empregador | não | sim | **40%** |
| Rescisão indireta | empregador | não | sim | **40%** |
| Pedido de demissão | **empregado** | não | sim | 0% |
| Dispensa por justa causa | ninguém | não | **não** | 0% |
| Acordo entre as partes | empregador | **sim** | sim | **20%** |
| ⚠️ Término por prazo determinado | — | — | — | **bloqueado** |
| ⚠️ Falecimento do empregado | — | — | — | **bloqueado** |
| ⚠️ Aposentadoria | — | — | — | **bloqueado** |

- **Bloqueado ≠ "gera zero".** Os três sem fonte não são calculados, e a resposta diz a
  razão. Devolver zero seria pior: um número com cara de exato sobre regra não confirmada.
  Mas o **contexto vem mesmo assim** — avos, dias, datas —, porque quem lê precisa entender
  o que falta.
- **A proporcionalidade do aviso não vale para os dois lados.** A Lei 12.506 lida sozinha
  sugere reciprocidade; a SDI-1 decidiu que não. Quem pede demissão deve **30 dias fixos** —
  e como quem deve é o empregado, **não há verba a pagar a ele**.
- **Justa causa perde as proporcionais, não as vencidas.** A exceção da Súmula 171 alcança
  o período incompleto; os completos eram direito adquirido antes da falta grave.
- **Férias proporcionais têm constante própria**: "superior a 14 dias" (CLT art. 146),
  enquanto o 13º usa "igual ou superior a 15" (Lei 4.090). Dão o mesmo número em dias
  inteiros — e é por isso que a tentação de reusar existe. São duas leis; se uma mudar, a
  outra não muda junto. Há teste travando que as constantes diferem.
- O **13º proporcional vira dinheiro**, em **duas** verbas: o proporcional e o que decorre
  da projeção do aviso. Separadas porque o 13º sobre o aviso indenizado tem INSS e FGTS mas
  **não tem IRRF** — numa linha só, a base do imposto sairia maior que a devida.
- Isso **não destravou a Fase 4F** na época — ela foi resolvida depois, por outro
  caminho (ver adiante). A dúvida de lá era sobre **quando** INSS e IRRF
  incidem no 13º — no adiantamento ou só na apuração anual. Na rescisão não há duas
  parcelas: há uma verba única, paga no acerto. A pergunta que bloqueia a 4F não se coloca
  aqui.
- **O aviso indenizado projeta a data de saída.** A CLT art. 487 § 1º manda contá-lo como
  tempo de serviço, e a Súmula 305 do TST diz que é o **término do aviso** que vai para a
  CTPS. Isso acrescenta avos de 13º e de férias — e era um defeito real: o cálculo parava
  na data de desligamento e a pessoa perdia o avo que a lei lhe dá.

> **O valor base do FGTS é informado, não calculado** — como no FGTS Digital. O saldo real
> da conta vinculada tem correção e juros que o produto não conhece; ele só sabe os
> depósitos que apurou. Calcular sobre isso daria um número **menor que o devido e com cara
> de exato**. O que o sistema conhece volta para **comparação**: se o informado ficar
> abaixo, a tela avisa — aviso, não recusa, porque o sistema não sabe o saldo real. Sem
> valor informado, **não há linha de multa**.
>
> Ele é **gravado no contrato** por `PUT`, com o valor no corpo — não é parâmetro de
> consulta. É um dado informado uma vez e reusado por toda apuração seguinte, e um número
> que multiplica dinheiro não pertence à query string, que vaza para log de servidor, log
> de proxy, histórico do navegador e cabeçalho `Referer`. Enquanto ninguém informa, a
> resposta traz `null` — "informei zero" e "não informei" são coisas diferentes.

**Incidências das verbas rescisórias**, conforme a tabela do **eSocial vigente em 2026**:

| Verba | INSS | IRRF | FGTS |
|---|:---:|:---:|:---:|
| Saldo de salário | Sim | Sim | Sim |
| Aviso prévio indenizado | Não | Não | **Sim** |
| Férias vencidas, proporcionais e em dobro, mais o 1/3 | Não | Não | Não |
| 13º proporcional | Sim | Sim | Sim |
| 13º sobre o aviso prévio indenizado | Sim | **Não** | Sim |

A folha de rescisão paga quem foi desligado na competência e **exige as nove rubricas
cadastradas** — faltando uma, a verba correspondente sairia do acerto em silêncio, então a
API recusa com **409** listando as que faltam. Contratos com motivo bloqueado são pulados,
e a resposta diz quantos ficaram de fora: um holerite vazio no meio da folha pareceria erro
de cálculo.

**Motivo do desligamento (Fase 4G, etapa 1)**

Até aqui o contrato desligava **sem motivo** — e o motivo é o campo que **decide as
verbas**: quem pede demissão não recebe aviso prévio indenizado nem multa de FGTS; quem é
dispensado por justa causa perde também as férias proporcionais; no acordo do art. 484-A
metade do aviso e metade da multa são devidas.

Fontes: **CLT art. 482** (justa causa), **art. 483** (rescisão indireta), **art. 484-A**
(acordo, Lei 13.467/2017) e **art. 443** (prazo determinado).

- O enum **não é a Tabela 19 do eSocial**. Aquela tem ~30 códigos e inclui situações que
  não mudam verba nenhuma — transferência entre empresas do grupo, mudança de CNPJ. Aqui
  estão os **oito motivos que o cálculo distingue**. O mapeamento para os códigos do
  eSocial é assunto de integração (Fase 8) e fica pendente: a Tabela 19 **não pôde ser
  lida** das fontes oficiais — o HTML trunca antes dela e os PDFs não são extraíveis.
- O motivo é **obrigatório**, validado **antes** de qualquer mutação: motivo inválido não
  deixa o contrato "meio desligado".
- Na tela o campo começa **em branco**. Um padrão convidaria a aceitar o que já estava lá,
  e o que está em jogo é quanto a pessoa recebe.
- **Não** há constraint exigindo motivo em contrato desligado: os encerrados antes desta
  fase ficam com motivo nulo, porque ninguém sabe por quê. Uma constraint obrigaria a
  **inventar** um motivo no backfill.
- **Não** há como alterar o motivo depois. Corrigi-lo é operação de correção, com efeito
  financeiro — entra quando a etapa 2 definir esse fluxo.

> A **tela de desligamento não existia**: `desligar` estava no cliente HTTP sem uso em
> página alguma, e o endpoint só era exercitado por teste desde a Fase 2. Agora há o
> formulário, com o motivo e o aviso de que não há reabertura.

**13º salário (Fase 4F)**

Fonte: **MOS eSocial S-1.3**, consolidado até a **NO S-1.3 – 10.2026**, itens 10.3.4 e
10.3.4.1, e a **Nota Orientativa 2018.13**. As duas dizem a mesma coisa:

> "A apuração da CP e do IRRF incidentes sobre o 13º salário é feita apenas na folha de 13º
> (anual)."

> "o FGTS, ao contrário da CP e do IRRF, incide sobre a parcela do adiantamento do 13º
> salário no mês em que for paga. (...) o FGTS incidente sobre a folha do 13º salário é
> calculado apenas sobre a diferença entre o valor da gratificação natalina e a primeira
> parcela."

São **duas folhas**, e não uma com duas etapas:

| | 1ª parcela (fev–nov) | Folha anual (dezembro) |
|---|---|---|
| INSS | não | **sim, sobre o total** |
| IRRF | não | **sim, sobre o total** |
| FGTS | **sim, sobre o adiantamento** | sim, **só sobre a diferença** |

Daí o problema mais interessante da fase: **três bases diferentes num holerite só**.
Incidência é atributo da rubrica, e uma rubrica tem uma declaração. A folha anual resolve
com três rubricas — o **total** (provento, INSS e IRRF), o **adiantamento compensado**
(desconto, sem incidência) e a **diferença** (informativa, só FGTS). Rubrica informativa
compõe base sem entrar no líquido: é exatamente para isso que o tipo existe, e a peça já
estava pronta desde a Fase 4A.

- **O total não declara FGTS de propósito.** Se declarasse, o Fundo incidiria sobre o 13º
  inteiro e o adiantamento seria tributado duas vezes — e a folha fecharia certa no
  líquido, sem nada parecer errado.
- **O adiantamento é metade do 13º proporcional**, não metade do salário cheio. A Lei
  4.749 diz "metade do salário do mês anterior", e ao pé da letra quem entrou em outubro
  receberia mais do que o 13º inteiro. O MOS admite esse caso, mas ele deixa o líquido de
  dezembro negativo — o padrão do produto é o conservador, e o caso maior continua
  suportado sem quebrar.
- **O adiantamento já pago é estado derivado**: a folha anual soma os lançamentos das
  folhas de adiantamento do mesmo ano. Nada é digitado.

> **A "contradição" que bloqueou esta fase por um dia não existia.** Duas páginas oficiais
> pareciam discordar sobre descontar INSS do adiantamento. Uma falava da **1ª parcela
> normal**; a outra, do caso excepcional de **antecipação integral antes de dezembro**, em
> que o empregador paga o **líquido** — mas onde a *apuração* continua sendo anual. Uma
> tratava de fluxo de caixa, a outra de apuração. A causa real do bloqueio foi um PDF
> declarado "não extraível" que `pdftotext` lê sem dificuldade.

**Avos de 13º (Fase 4F, etapa 1)**

Fontes: **Lei nº 4.090/1962** (1/12 por mês de serviço; fração **≥ 15 dias** conta como mês
integral) e **Lei nº 4.749/1965** (pagamento até 20/12, adiantamento entre fevereiro e
novembro).

- Os avos **não têm tabela**, como os períodos aquisitivos de férias: são função pura da
  admissão, do desligamento e do calendário.
- **Reusa `MotorCalculoFolha.PeriodoNaCompetencia`** — a pergunta *"quantos dias o contrato
  esteve vigente neste mês?"* já era respondida pelo motor da folha mensal, e duas contas
  separadas para a mesma pergunta acabariam divergindo.
- O teste dos 15 dias é **`>=`**: admitido em **17 de março** são 15 dias exatos e o mês
  **conta**; em 18 de março são 14 e não conta. Há teste travando os dois lados da
  fronteira — um erro de `>=` para `>` tiraria um avo de quem entrou no dia 17.
- A resposta traz **os doze meses**, cada um com os dias e o **motivo** em português.
  Mostrar só "9/12" deixaria o analista sem saber se é o mês da admissão, o do
  desligamento ou um erro de cadastro.

> **Limitação declarada:** **afastamentos não são considerados** — o domínio não tem
> afastamento, pelo mesmo motivo das faltas nas férias. Um mês com afastamento além do 15º
> dia não deveria contar, e aqui conta.

**Pagamento de férias (Fase 4E, etapa 2b)**

Fontes: **CLT art. 142** (remuneração devida na data da concessão), **CF art. 7º, XVII**
(um terço a mais), **CLT art. 143** (abono) e o **Manual do eSocial** para as incidências.

| Verba | Rubrica | INSS | IRRF | FGTS |
|---|---|:--:|:--:|:--:|
| Férias gozadas | `FER` | Sim | Sim | Sim |
| Terço sobre férias gozadas (eSocial 1920) | `FER13` | Sim | Sim | Sim |
| Abono pecuniário | `ABONO` | Não | Sim | Não |
| Terço sobre o abono (eSocial 1940) | `ABN13` | Não | Sim | Não |

- **Quatro rubricas, não duas** — e a razão é a tabela acima: as duas linhas de terço têm
  incidências **diferentes**. Com uma rubrica de terço só, seria preciso escolher uma das
  duas e errar a outra em todo holerite com abono. Há teste travando que os dois conjuntos
  diferem, para que um copiar-colar quebre o teste em vez do imposto de todo mundo.
- **`TipoFolha`** entrou em `FolhaPagamento`, e a coluna foi para o **índice único**: a
  mesma empresa pode ter, em agosto, a folha mensal **e** a de férias.
- O critério é a **data de início do gozo**, não o período aquisitivo: quem sai em 02/01 é
  pago na folha de janeiro, mesmo que o período seja de dois anos atrás.
- O **salário é o da data da concessão** (art. 142). Quem recebeu aumento entre o período
  aquisitivo e o gozo goza com o salário novo.
- O terço incide sobre o valor **arredondado** — é o número do holerite, e a pessoa precisa
  conseguir refazer a conta à mão.
- **Divisor 30, sempre.** Usar os dias do mês faria o mesmo funcionário receber valores
  diferentes pelos mesmos 30 dias.
- Faltando alguma das quatro rubricas, o cálculo **recusa** (409, listando quais) em vez de
  pagar menos em silêncio.

> ⚠️ **Limitação importante — IRRF apurado sobre a folha de férias isolada.** O imposto é
> calculado sobre ela mesma, sem somar a folha mensal do mesmo mês. Quando as duas
> coexistem, isso **subestima o imposto**: a tabela é progressiva, e dois rendimentos
> separados caem em faixas mais baixas do que a soma cairia. Somar as duas exige decidir
> em qual folha o imposto é retido e como reprocessar. **Resolver antes de qualquer uso
> real.**
>
> Isso **não alcança o 13º**, ao contrário do que este texto dizia antes: o 13º tem
> **tributação exclusiva na fonte** e é apurado em separado por determinação legal — ali,
> separar é o certo.

> **Outras limitações:** a **dobra do art. 137** não é calculada — o período vencido é
> identificado e a tela avisa, mas o pagamento em dobro não é aplicado, porque falta
> decidir se o terço também dobra. **Férias coletivas** (art. 139) continuam fora.

**Concessão de férias (Fase 4E, etapa 2a)**

Fontes: **CLT art. 134, §1º** (até três períodos, um ≥ 14 dias corridos e os demais ≥ 5) e
**art. 143** (venda de até 1/3 em abono pecuniário).

- A concessão **tem** tabela, ao contrário do período: ela existe porque alguém decidiu
  conceder, e essa decisão não se recalcula.
- O período é referenciado pelas **datas**, não por um id. Consequência deliberada:
  corrigir a admissão desloca os períodos, e uma concessão órfã fica **visível** — melhor
  do que apontar em silêncio para o período errado.
- O período é **procurado entre os derivados**, nunca aceito como o cliente mandou. Data
  inventada não encontra período.
- As recusas vêm **todas de uma vez** e **citam o artigo**: quem preenche o formulário
  merece ver tudo que está errado, e quem recebe a recusa costuma precisar justificá-la.
- A regra dos 14 dias só é cobrada **ao fechar** o período: 5 dias em janeiro e 25 em
  julho cumprem a lei, e recusar a primeira metade impediria uma programação legítima.
- **Abono puro não conta** como uma das três frações — vender dias não é gozar.
- Cancelar só **antes** de começar (**409** depois): envolve retorno ao trabalho e acerto
  do que foi pago.

> **Limitações declaradas:** o **art. 134, §2º** — proibição de iniciar férias nos dois
> dias antes de feriado ou repouso — exigiria um calendário de feriados, que o domínio não
> tem. **Férias coletivas** (art. 139) não existem.

**Direito a férias (Fase 4E, etapa 1)**

Fontes: **CLT art. 130** (12 meses dão direito), **art. 134** (concessão nos 12 meses
subsequentes) e **art. 137** (fora do prazo, remuneração **em dobro**).

- O período aquisitivo **não tem tabela no banco**, e isso é decisão registrada: ele é
  função pura da data de admissão e do calendário. Persistir criaria linhas cujo único
  conteúdo é o que o próprio cálculo produziria — e que poderiam divergir da admissão se
  ela fosse corrigida. Quem tem estado é a **concessão**, que ainda não existe.
- A situação é uma **pergunta feita numa data**, não uma coluna: um período "Adquirido"
  vira "Vencido" pela simples passagem do tempo. Por isso a API aceita `?referencia=`, e
  *"em dezembro, quantos estarão vencidos?"* se responde sem simular nada.
- O **último dia** do prazo de concessão ainda não é dobra. Um erro de `>` para `>=` aqui
  pagaria em dobro férias concedidas no prazo — e ninguém reclamaria, porque o erro é
  contra a empresa.
- Contrato desligado **para de gerar períodos**: o que sobra vira férias proporcionais,
  que são verba rescisória (Fase 4G).
- A tela avisa em destaque quando há período vencido: é dinheiro a mais que a empresa vai
  pagar por não ter concedido no prazo.

> **Limitações declaradas:** a **redução por faltas** (art. 130) não é aplicada porque o
> domínio **não tem faltas** — não há registro de ausência em lugar nenhum, então não há o
> que contar. Todo período completo dá 30 dias. **Tempo parcial** (art. 130-A) não é
> suportado: tem tabela própria, e o contrato guarda jornada mensal, não semanal.
> **Férias coletivas, abono pecuniário e o 1/3** pertencem à etapa 2.

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

Afastamentos, importações, motor de análises, integrações, recursos AWS e CI/CD. Tudo isso
pertence às fases seguintes.

Os **cinco tipos de folha** calculam: mensal, férias, rescisão, adiantamento de 13º e a
folha anual de 13º.

A **folha mensal** e a **de férias** estão completas. Do 13º existe o direito (os avos),
não o pagamento.

> ⚠️ O pagamento do 13º está **bloqueado por uma contradição entre fontes oficiais**: uma
> nota do eSocial/FGTS Digital diz que INSS e IRRF incidem **apenas na apuração anual**,
> sobre o total, enquanto a página *"Como pagar a primeira parcela do 13º"* manda
> descontá-los **do adiantamento**. A segunda é provavelmente do regime **doméstico**, mas
> a página não deixa isso explícito e os PDFs técnicos não são extraíveis. Concluir por
> eliminação seria interpretação, não implementação.

Cada encargo exige fonte oficial registrada e parâmetro versionado (`CLAUDE.md §29`). A
Fase 4A criou as bases, a 4B aplicou a primeira alíquota, a 4C acrescentou o depósito do
empregador e a 4D fechou o imposto de renda. **A folha mensal está completa**, e as folhas
de **férias (4E)** e de **rescisão (4G)** também. O **13º (4F)** continua bloqueado por
contradição entre fontes oficiais sobre o momento da incidência.

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
| `GET /api/contratos/{id}/ferias/periodos` | todos os 5 | Períodos aquisitivos, com saldo e concessões |
| `POST /api/contratos/{id}/ferias/concessoes` | Adm. Empresa, Analista | Programa férias de um período |
| `DELETE /api/contratos/{id}/ferias/concessoes/{idC}` | Adm. Empresa, Analista | Cancela uma programação que não começou |
| `POST /api/folhas` (campo `tipo`) | Adm. Empresa, Analista | Abre folha **Mensal**, **Ferias** ou **Rescisao** |
| `GET /api/contratos/{id}/decimo-terceiro/avos` | todos os 5 | Avos de 13º no ano, mês a mês, com o motivo |
| `POST /api/contratos/{id}/desligamento` (campo `motivo`) | Adm. Empresa, Analista | Encerra o vínculo, com o motivo obrigatório |
| `GET /api/contratos/{id}/rescisao` | todos os 5 | Apura as verbas, usando o valor base gravado |
| `PUT /api/contratos/{id}/rescisao/valor-base-fgts` | Adm. Empresa, Analista | Grava o valor base do FGTS rescisório |
| `GET /api/contratos/{id}/rescisao/matriz` | todos os 5 | O que cada motivo gera, com a fonte |
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
