# Evidências — auditoria de portfólio, outubro de 2026

**Período:** 08 a 11/10/2026 · **Base auditada:** `4bae6b3` (nota atribuída: **80/100**) ·
**Commit das correções:** ver `git log` a partir de `5b021a4`.

Uma auditoria externa listou oito itens (PR-01 a PR-08) para o projeto ser avaliado como
portfólio sênior. Este documento registra o que foi feito em cada um, **com a medida**, e
o que continua de fora. O plano original da auditoria ficou fora do repositório por
decisão do responsável; os itens estão descritos aqui por completo.

Nenhuma funcionalidade de domínio nova entrou. **Nenhuma regra de cálculo, parâmetro legal
ou arredondamento mudou**, e não houve migration.

---

## PR-01 — Folha fechada imutável sob concorrência

**O defeito.** `FolhaPagamento.GarantirAberta` conferia o estado que a requisição *leu*.
Uma requisição que leu a folha aberta podia gravar depois de outra fechá-la.

**Reprodução antes da correção** — `FolhaConcorrenciaTestes`, PostgreSQL 17 real via
Testcontainers, contra o código de `4412354`:

```text
Com falha: 13, Aprovado: 1, Total: 14
  lancar em folha que outra transacao fechou ......... 200 (esperado 409)
  remover lancamento idem ............................ 204 (esperado 409)
  recalcular idem .................................... 200 (esperado 409)
  fechar enquanto um lancamento grava ................ "terminou sem esperar a trava"
  dois fechamentos simultaneos ....................... os dois 200, duas auditorias
  falha forcada depois do SQL, antes do commit ....... folha ficou Fechada, lancamento ficou
  trava presa ........................................ requisicao parada ate o timeout do teste
```

O único verde era o de outra organização — que continua no arquivo como guarda de
regressão.

**A decisão.** Trava de linha (`SELECT ... FOR UPDATE`) na folha, dentro de transação
explícita, **antes** de carregar o agregado; um único caminho de escrita
(`FolhasEndpoints.AlterarFolhaAsync`) com commit num lugar só e a auditoria dentro da
mesma transação. Alternativas recusadas e o motivo de cada uma:
[ADR 009](adr/009-trava-pessimista-na-folha.md).

**Depois da correção:**

| Cenário (PostgreSQL real, ordem escolhida pelo teste) | Resultado |
|---|---|
| Folha fechada por outra transação enquanto lançar / lançar informativo de valor 0 / remover / recalcular esperavam | **409** com a mensagem do domínio; versão, totais e ids de lançamento **idênticos** aos de antes |
| Fechar chega enquanto lançar / remover / recalcular gravam | o fechamento **espera**; a auditoria registra exatamente `liquido` e `versao` gravados |
| Dois fechamentos simultâneos | um 200, um 409, **uma** auditoria |
| Duas edições simultâneas | as duas ficam; totais somam as duas |
| Falha forçada depois do SQL e antes do commit (fechar e lançar) | **500**, e nada ficou: situação, versão, líquido, lançamentos e auditoria iguais aos de antes; a operação seguinte passa sem esperar |
| Trava segurada além de 5 s | **409 "Folha em uso"**, nada gravado |
| Outra organização com a folha travada | **404 imediato** — não entrou na fila da trava alheia |

Como a ordem é controlada: um interceptor de teste (`PortaoDoSalvar`) estaciona a
requisição entre "apliquei o domínio" e "gravei"; a espera é por condição observada no
banco (`pg_stat_activity.wait_event_type = 'Lock'`), sempre com prazo máximo de 30 s.
Nenhum *sleep* torcendo pela ordem.

**Persistência de todos os tipos de cálculo.** O ajudante de cálculo das classes de teste
de folha mensal, férias, 13º e rescisão passou a reler a folha numa requisição nova e
exigir que o que voltou no 200 esteja gravado — um 200 sozinho não prova commit.

**Estabilidade:** a classe rodou 3 vezes seguidas e uma em modo invariante, 14/14 em
todas.

**Tela:** 409 em calcular, fechar, lançar ou remover recarrega a folha e o holerite antes
de mostrar a mensagem (`FolhaDetalhe.teste.tsx`, dois testes).

---

## PR-02 — Dependências e instalação limpa

| | Antes (08/10) | Depois (11/10) |
|---|---|---|
| `npm audit` completo | 16 pacotes: 1 crítica, 11 altas, 4 moderadas | **0** |
| `npm audit --omit=dev` | 1 alta (`source-map-js`) | **0** |
| `npm ls` | 6 pacotes *invalid* (lockfile do Dependabot nunca reinstalado) | sem erro, após `npm ci` |
| NuGet, 6 projetos com transitivas | — | **0** vulneráveis |

- 12 dos 16 vinham **só** do CLI `shadcn` — gerador de componentes, não código do
  produto. Um deles (`braces`, GHSA-vfj7-8cjw-p6xm) **não tem versão corrigida**: a saída
  era retirar o CLI, não atualizá-lo.
- O build usava `shadcn/tailwind.css`. O arquivo foi copiado da versão do lockfile
  (4.19.1, conferida com `npm ls` depois de `npm ci`), com a licença MIT ao lado. **O CSS
  gerado ficou idêntico** — mesmo SHA-256 antes e depois.
- `source-map-js` 1.2.1 → 1.2.2 e `undici` 7.29.0 → 7.30.0 por `npm audit fix` **sem**
  `--force`, dentro das faixas já declaradas; 291 pacotes saíram do lockfile, **nenhum
  entrou**.

Separação pedida: `source-map-js` aparecia como "produção" só porque o plugin do Tailwind
está em `dependencies`; ele roda no **build**, não no navegador.

---

## PR-03 — Holerite e tabelas pelo teclado

- A linha era um `<tr onClick>` que o Tab nunca alcança. Agora a coluna principal das
  **seis** tabelas clicáveis é elemento nativo: `<a>` para navegar (Folhas, Funcionários),
  `<button>` para abrir painel (holerite, empresa, importação, inconsistência). Enter e
  Space são do próprio elemento.
- Nome acessível começa pela ação e contém o texto visível: *"Abrir holerite de Bruno
  Carvalho Lima"* (WCAG 2.5.3). Um teste pegou que o espaço entre os dois era descartado —
  virava "deBruno".
- O clique na linha continua como atalho de mouse e aciona o mesmo elemento, uma vez só.
- Painéis abertos sem `DrawerTrigger` devolviam o foco ao `<body>`. `DrawerContent` guarda
  quem tinha o foco ao abrir e o devolve ao fechar — holerite, memória de cálculo,
  tratamento. O foco continua preso dentro do painel modal (Radix).
- `LimiteDeErro` deixou de abrir um `<main>` dentro do `<main>` do shell.

Testes com `user-event`: Tab até "Abrir holerite", Enter abre, Escape fecha e o foco
volta; Space abre; Tab até a rubrica, Enter abre a memória, Escape devolve o foco à
rubrica; link de tabela navegando por Enter.

---

## PR-05 — Carregamento inicial

Mesmo Node (22.17.0), mesmas dependências, `npm run build`. Medida: **todos** os arquivos
que o `index.html` manda baixar na primeira visita (script inicial, *preload* e CSS),
comprimidos com `gzip -9`.

| | Arquivos | Crus | gzip |
|---|---|---:|---:|
| Antes | `index.js` + `index.css` | 600.054 B | 166.969 B |
| Depois | `index.js` + `input.js` (preload) + `index.css` | 441.345 B | 133.357 B |
| | | **−26,4%** | **−20,1%** |

- Cada página é `React.lazy`; `Entrar` ficou no arquivo inicial para o login não esperar um
  segundo download. Telas pesadas (FuncionarioDetalhe 32 kB, FolhaDetalhe 23 kB,
  Importações 18 kB) só baixam quando abertas.
- O aviso de *chunk* acima de 500 kB **sumiu sem subir o teto** — o `chunkSizeWarningLimit`
  continua no padrão.
- Falha ao baixar uma página (rede, ou deploy novo com a aba aberta) vira "Recarregar a
  página", e não tela branca: `React.lazy` guarda a falha, e um "Tentar novamente" a
  repetiria. Teste com um import que rejeita.

---

## PR-07 — Demonstração

- Login e rota desconhecida levam ao **Painel**.
- Para o Visualizador (conta pública, somente leitura), o Painel abre com o **Roteiro da
  demonstração**: folha → holerite → bases → memória → inconsistências → auditoria.
- O link da folha **vem da API**: o roteiro procura o achado `LiquidoNegativo` e aponta
  para a folha onde ele foi detectado. Sem o achado, aponta para a lista; falhar na busca
  não derruba o painel.
- O cartão explica que o líquido negativo está lá **de propósito** — é o que a conferência
  existe para pegar — e que o cálculo não foi ajustado para escondê-lo. A folha negativa
  da demo **não** foi apagada nem "corrigida".
- Nenhuma escrita liberada para o perfil público: `VisualizadorSomenteLeituraTestes`
  continua exigindo 403 em toda rota de escrita, descoberta em tempo de execução.

---

## PR-04 — Documentação

- `README.md`: 1.483 → ~290 linhas. Histórico de fases e tabela de endpoints **movidos**,
  não apagados, para [historico-de-entregas.md](historico-de-entregas.md), com aviso de que
  são registros datados. Cada item que saiu do README foi conferido no destino; os que não
  tinham destino (porta 5433, `VITE_API_URL`, CPF mascarado, Security Gate por fase,
  `DESIGN.md`) voltaram em forma curta.
- Corrigido o que o código já contradizia: "não existe rate limiting"; `SameSite=Lax` e
  API Gateway; paginação; cookie CSRF "legível por JS" (no README **e** no comentário do
  `GuardaCsrf`); rate limit "por organização" no login (ADR 006); concorrência reservada
  (arquitetura).
- **"Um XSS não rouba a sessão"** saiu do README e do ADR 004. Cookie `HttpOnly` limita o
  que um XSS leva embora; não impede que ele aja como a página enquanto ela está aberta.
- 20 nomes de arquivo citados no README conferidos contra o repositório: todos existem.
- `CLAUDE.md §24.19` item 13 e `§4.4`; `ROADMAP.md`, seção "Auditoria de portfólio".
- [publicacao.md](publicacao.md) e `scripts/empacotar-lambda.py`: o procedimento de
  publicação saiu do scratchpad para o repositório, com a reversão de cada passo. O script
  recusa `bootstrap` que não seja ELF, grava o zip com origem Unix e modo 0755 — o zip
  montado no Windows registra origem MS-DOS, e o Linux ignora a permissão — e deixa
  `appsettings.Development.json` fora do pacote.

---

## PR-06 — Verificação final

No commit das correções, em 11/10/2026, SDK 10.0.401, Node 22.17.0:

| Verificação | Resultado |
|---|---|
| Build backend Release `-warnaserror` | **0 avisos, 0 erros** |
| Suíte backend (Testcontainers, PostgreSQL 17) | **1300/1300** — eram 1286 |
| A mesma suíte com `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` | **1300/1300** |
| `FolhaConcorrenciaTestes` repetida | 3× 14/14 + 1× 14/14 invariante |
| `dotnet ef migrations has-pending-model-changes` | *"No changes have been made to the model since the last migration"* |
| NuGet vulnerável (6 projetos, transitivas) | 0 |
| `npm ci` · `npm ls` · lint | limpos |
| Testes frontend, 3 vezes | **185/185** nas três — eram 171 |
| Build frontend | sem aviso de tamanho |
| `npm audit` completo e `--omit=dev` | 0 e 0 |
| Varredura de segredos nos arquivos versionados | nenhum indício |

Preservados sem alteração: `MotorCalculoFolhaTestes`, `IrrfFeriasEmSeparadoTestes`,
`VisualizadorSomenteLeituraTestes` e toda a suíte de isolamento. A IA da demo **não** foi
chamada para gerar evidência; nenhum custo foi criado.

---

## Reavaliação pela rubrica da auditoria

Nota por evidência, não por tarefa cumprida.

| Dimensão | Peso | Nota | Por quê |
|---|---:|---:|---|
| Correção e integridade financeira | 25 | **24** | Corrida reproduzida e fechada no banco, com auditoria atômica e persistência conferida em todos os tipos. Cálculo intocado. Fica 1 ponto: afastamentos e três motivos de rescisão continuam fora, por falta de domínio ou de fonte oficial |
| Segurança | 20 | **19** | Audits zerados sem enfraquecer gate; trava respeita tenant; afirmação de XSS corrigida; credencial de desenvolvimento fora do pacote. Residual: sem WAF (custo), `style-src 'unsafe-inline'` |
| Testes e reprodução | 20 | **19** | Instalação limpa, dois modos, corrida com ordem controlada e o vermelho registrado antes do verde. Falta E2E em navegador automatizado |
| Arquitetura e manutenção | 15 | **14** | Um caminho de escrita, ADR com alternativas recusadas, publicação versionada. `FolhasEndpoints.cs` continua grande |
| Demonstração, acessibilidade e desempenho | 10 | **9** | Teclado, foco, roteiro e −20% na primeira visita. A verificação em produção está na seção seguinte |
| Documentação e evidências | 10 | **10** | Entrada curta e conferida contra o código, histórico preservado, este documento |
| **Total** | 100 | **95** | |

Não é promessa de conformidade fiscal nem de segurança absoluta: é o que os testes e as
medidas acima sustentam.

## Limitações que continuam

- Afastamentos não existem no domínio; três motivos de rescisão seguem bloqueados por
  falta de fonte oficial — o sistema diz isso em vez de chutar.
- Sem E2E automatizado em navegador; a verificação em produção é manual.
- `FolhasEndpoints.cs` não foi dividido: tamanho sozinho não justificava reescrever o
  arquivo no meio de uma correção de integridade.
- Rota nova que escreva na folha precisa passar por `AlterarFolhaAsync` — a garantia é de
  construção, e o teste cobre os caminhos que existem hoje.
- O PR do Dependabot NuGet que falhou no CI em 07/10 (run 37595199481) não foi tratado
  aqui.

## Verificação em produção — 11/10/2026

Publicado **depois** do CI do commit `abf09e9` terminar verde (run 38155102846: backend
nos dois modos, NuGet, frontend com `npm audit`, varredura de segredos). Nenhuma
migration; nenhum recurso AWS criado; custo **US$ 0,00**.

| Passo | Evidência |
|---|---|
| Reversão guardada antes | zip das duas Lambdas baixado, SHA-256 igual ao `CodeSha256` da AWS |
| Pacotes | `scripts/empacotar-lambda.py` a partir de `abf09e9`: `bootstrap` ELF, origem Unix, 0755 |
| API | `update-function-code` → `wait function-updated-v2` → `Successful`, `CodeSha256` **igual** ao do zip enviado; `/health` → `{"status":"saudavel"}` (inclui banco e migrations) |
| Worker | idem, `Successful` e hash igual. Invocação sintética com mensagem malformada: 200, sem `FunctionError`, `worker pronto ... ssl=VerifyFull` (sem senha no log) e `mensagem recusada no esquema: VersaoDesconhecida`, `batchItemFailures: []` |
| Frontend | Vercel `--prod`, `READY`; o domínio público serve o `index.html` novo, com o *preload* da divisão por rota; link direto para `/folhas/<id>` → 200; CSP, HSTS e `X-Frame-Options` intactos |
| Telas novas no ar | o pedaço `Painel-*.js` servido contém o "Roteiro da demonstração" e a explicação do líquido negativo; `FolhaDetalhe-*.js` contém "Abrir holerite de" e o recarregamento no 409 |
| CORS e autorização | preflight da Vercel: 204 com `allow-origin` exato e credenciais; origem estranha: nenhum cabeçalho CORS; `fechar` e `lancar` sem token: **401** |

⚠️ **Não verificado, e por quê:** o percurso visual no navegador — entrar pelo botão da
demonstração, Tab até o holerite, Escape devolvendo o foco, larguras de 390, 768 e
1280 px e o console — **não foi executado em produção**: a extensão de automação do
navegador não estava conectada nesta sessão, e o agente não digita senha em campo. O
mesmo comportamento é coberto pelos testes de interface com `user-event` (185, três
vezes), mas teste em `jsdom` não substitui olhar a tela. Para conferir em 3 minutos:
abrir a aplicação, **Entrar na demonstração**, seguir o roteiro do Painel usando só o
teclado.

Por isso a nota de "Demonstração, acessibilidade e desempenho" fica em **9**, e não 10.
