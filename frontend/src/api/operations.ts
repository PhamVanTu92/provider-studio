import { apiFetch } from './client'

export interface ParamMapping {
  id: string; jsonPath: string; paramName: string
  paramType: string; isRequired: boolean; defaultValue: string; sortOrder: number
  apiTarget: string  // query | body | path | header
}

export interface Operation {
  id: string; providerId: string; dbConnectionId: string
  pattern: string; mode: 'get' | 'push'
  queryType: 'view' | 'storedproc' | 'function' | 'rawsql'
  queryTarget: string; pushPollIntervalSeconds: number
  pushChangeQuery: string; isEnabled: boolean; createdAt: string
  paramMappings: ParamMapping[]
}

export const listOperations  = (pid: string) => apiFetch<Operation[]>(`/api/providers/${pid}/operations`)
export const getOperation    = (pid: string, id: string) => apiFetch<Operation>(`/api/providers/${pid}/operations/${id}`)
export const createOperation = (pid: string, body: object) => apiFetch<Operation>(`/api/providers/${pid}/operations`, { method: 'POST', body: JSON.stringify(body) })
export const updateOperation = (pid: string, id: string, body: object) => apiFetch<Operation>(`/api/providers/${pid}/operations/${id}`, { method: 'PUT', body: JSON.stringify(body) })
export const deleteOperation = (pid: string, id: string) => apiFetch<void>(`/api/providers/${pid}/operations/${id}`, { method: 'DELETE' })
export const testOperation   = (pid: string, id: string, paramsJson?: string) =>
  apiFetch<{ ok: boolean; elapsedMs?: number; result?: string; error?: string }>(
    `/api/providers/${pid}/operations/${id}/test`,
    { method: 'POST', body: JSON.stringify({ paramsJson }) })
