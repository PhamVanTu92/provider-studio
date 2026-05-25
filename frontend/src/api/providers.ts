import { apiFetch } from './client'

export interface Provider {
  id: string; name: string; clientId: string
  tokenEndpoint: string; bridgeGrpcUrl: string
  ingestionBaseUrl: string; ingestionTokenEndpoint: string
  version: string; isEnabled: boolean; createdAt: string
}

export interface ProviderWithStatus {
  provider: Provider
  status: RuntimeStatus | null
}

export interface RuntimeStatus {
  providerId: string; providerName: string
  state: 'Stopped' | 'Connecting' | 'Connected' | 'Error' | 'Backoff'
  sessionId?: string; connectedAt?: string; lastHeartbeatAt?: string
  inflightRequests: number; reconnectAttempt: number
  lastError?: string; operationsHandled: number
}

export const listProviders  = () => apiFetch<ProviderWithStatus[]>('/api/providers')
export const getProvider    = (id: string) => apiFetch<Provider>(`/api/providers/${id}`)
export const createProvider = (body: object) => apiFetch<Provider>('/api/providers', { method: 'POST', body: JSON.stringify(body) })
export const updateProvider = (id: string, body: object) => apiFetch<Provider>(`/api/providers/${id}`, { method: 'PUT', body: JSON.stringify(body) })
export const deleteProvider = (id: string) => apiFetch<void>(`/api/providers/${id}`, { method: 'DELETE' })
export const testAuth       = (id: string) => apiFetch<{ ok: boolean; status?: number; error?: string }>(`/api/providers/${id}/test-auth`, { method: 'POST' })

export const startProvider   = (id: string) => apiFetch<{ message: string }>(`/api/runtime/${id}/start`, { method: 'POST' })
export const stopProvider    = (id: string) => apiFetch<{ message: string }>(`/api/runtime/${id}/stop`, { method: 'POST' })
export const restartProvider = (id: string) => apiFetch<{ message: string }>(`/api/runtime/${id}/restart`, { method: 'POST' })
export const getRuntimeStatus = () => apiFetch<RuntimeStatus[]>('/api/runtime/status')
