import type { Perfil } from './autenticacao'
import { enviar, obter } from './cliente'

export type Severidade = 'Baixa' | 'Media' | 'Alta'

export type CategoriaRegra = 'Contrato' | 'Ausencia' | 'Valores' | 'Duplicidade' | 'Salario'

export type TipoParametro = 'Decimal' | 'Percentual' | 'Inteiro'

/**
 * Um parâmetro de regra, com a faixa que o **servidor** declarou.
 *
 * A tela usa `minimo`/`maximo` para dar boa experiência no campo. Quem decide
 * é o backend: há teste de integração provando que 150 num campo de 1 a 100
 * volta 400 mesmo que o navegador tenha deixado digitar.
 */
export interface ParametroRegra {
  chave: string
  rotulo: string
  explicacao: string
  tipo: TipoParametro
  padrao: string
  minimo: string
  maximo: string
  valor: string
}

export interface Regra {
  codigo: string
  nome: string
  explicacao: string
  categoria: CategoriaRegra
  versao: number
  ativa: boolean
  severidade: Severidade
  severidadePadrao: Severidade
  /** Falso quando a organização nunca mexeu: a regra roda ativa, no padrão. */
  configurada: boolean
  alteradoEm: string | null
  parametros: ParametroRegra[]
}

export interface ResultadoAnalise {
  id: string
  codigo: string
  regra: string
  versaoRegra: number
  categoria: CategoriaRegra
  severidade: Severidade
  idFolhaFuncionario: string | null
  matricula: string | null
  nomeFuncionario: string | null
  descricao: string
  valorEsperado: number | null
  valorEncontrado: number | null
  diferenca: number | null
  contexto: string | null
}

export interface ExecucaoAnalise {
  id: string
  idFolha: string
  competencia: string
  versaoCalculoDaFolha: number
  executadaEm: string
  regrasExecutadas: number
  totalResultados: number
  resultadosAltos: number
  resultadosMedios: number
  resultadosBaixos: number
  /** A folha foi recalculada depois desta análise: ela não fala mais da folha que está no ar. */
  desatualizada: boolean
  resultados: ResultadoAnalise[] | null
}

export interface PaginaExecucoes {
  total: number
  pagina: number
  itens: ExecucaoAnalise[]
}

export const ROTULO_SEVERIDADE: Record<Severidade, string> = {
  Alta: 'Alta',
  Media: 'Média',
  Baixa: 'Baixa',
}

export const ROTULO_CATEGORIA: Record<CategoriaRegra, string> = {
  Contrato: 'Contrato',
  Ausencia: 'Ausência',
  Valores: 'Valores',
  Duplicidade: 'Duplicidade',
  Salario: 'Salário',
}

export const TOM_SEVERIDADE: Record<Severidade, 'critico' | 'atencao' | 'info'> = {
  Alta: 'critico',
  Media: 'atencao',
  Baixa: 'info',
}

export const SEVERIDADES: Severidade[] = ['Alta', 'Media', 'Baixa']

/**
 * Quem configura regra de análise.
 *
 * O Security Gate da Fase 6 separa três níveis: configurar é administração,
 * executar é operação, consultar é leitura. Afrouxar uma tolerância é o jeito
 * mais barato de fazer uma divergência sumir do relatório — quem roda a análise
 * no dia a dia não configura.
 *
 * Adaptação de interface, não autorização: quem barra é a política do backend.
 */
export function podeConfigurarRegras(perfil: Perfil | undefined): boolean {
  return perfil === 'AdministradorPlataforma' || perfil === 'AdministradorEmpresa'
}

export async function listarRegras(): Promise<Regra[]> {
  return obter<Regra[]>('/api/regras-analise')
}

export async function configurarRegra(
  codigo: string,
  dados: { ativa: boolean; severidade: Severidade; parametros: Record<string, string> },
): Promise<Regra> {
  return enviar<Regra>(`/api/regras-analise/${codigo}`, dados, 'PUT')
}

export async function analisarFolha(idFolha: string): Promise<ExecucaoAnalise> {
  return enviar<ExecucaoAnalise>(`/api/folhas/${idFolha}/analisar`, {})
}

export async function listarExecucoes(idFolha: string): Promise<PaginaExecucoes> {
  return obter<PaginaExecucoes>(`/api/folhas/${idFolha}/analises`)
}

export async function obterExecucao(id: string): Promise<ExecucaoAnalise> {
  return obter<ExecucaoAnalise>(`/api/analises/${id}`)
}
