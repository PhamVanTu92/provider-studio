import { apiFetch } from './client'

export interface DbConnection {
  id: string; providerId: string; name: string
  sourceType: string         // "database" | "api"
  // DB source fields
  dbType: string; host: string; port: number
  database: string; username: string; extraOptions: string
  // API source fields
  apiBaseUrl: string; apiAuthType: string; apiDefaultHeaders: string
  createdAt: string
}

export const listConnections  = (providerId: string) => apiFetch<DbConnection[]>(`/api/providers/${providerId}/connections`)
export const getConnection    = (pid: string, id: string) => apiFetch<DbConnection>(`/api/providers/${pid}/connections/${id}`)
export const createConnection = (pid: string, body: object) => apiFetch<DbConnection>(`/api/providers/${pid}/connections`, { method: 'POST', body: JSON.stringify(body) })
export const updateConnection = (pid: string, id: string, body: object) => apiFetch<DbConnection>(`/api/providers/${pid}/connections/${id}`, { method: 'PUT', body: JSON.stringify(body) })
export const deleteConnection = (pid: string, id: string) => apiFetch<void>(`/api/providers/${pid}/connections/${id}`, { method: 'DELETE' })
export const testConnection   = (pid: string, id: string) => apiFetch<{ ok: boolean; message?: string; error?: string }>(`/api/providers/${pid}/connections/${id}/test`, { method: 'POST' })
