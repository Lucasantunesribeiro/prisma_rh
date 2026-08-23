/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** URL base da API do Prisma RH. Ex.: http://localhost:5080 */
  readonly VITE_API_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
