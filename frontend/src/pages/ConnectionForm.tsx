import { useState, useEffect } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { getConnection, createConnection, updateConnection, testConnection } from '../api/connections'

// ─── Field types ──────────────────────────────────────────────────────────────

interface DbFields {
  dbType: string; host: string; port: number
  database: string; username: string; password: string; extraOptions: string
}

interface ApiFields {
  apiBaseUrl: string; apiAuthType: string; apiAuthValue: string; apiDefaultHeaders: string
}

interface Fields extends DbFields, ApiFields {
  name: string
  sourceType: 'database' | 'api'
}

const DB_PORTS: Record<string, number> = { postgresql: 5432, sqlserver: 1433, mysql: 3306 }

const DEFAULTS: Fields = {
  name: '', sourceType: 'database',
  // DB
  dbType: 'postgresql', host: 'localhost', port: 5432,
  database: '', username: '', password: '', extraOptions: '{}',
  // API
  apiBaseUrl: '', apiAuthType: 'none', apiAuthValue: '', apiDefaultHeaders: '{}',
}

const AUTH_LABELS: Record<string, string> = {
  none: 'No Authentication',
  bearer: 'Bearer Token',
  apikey: 'API Key (X-Api-Key header)',
  basic: 'Basic Auth (base64 user:pass)',
}

const AUTH_VALUE_LABELS: Record<string, string> = {
  none: '',
  bearer: 'Token',
  apikey: 'API Key',
  basic: 'Base64 credentials (user:pass)',
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function ConnectionForm() {
  const { pid, id } = useParams<{ pid: string; id: string }>()
  const isEdit = !!id
  const nav = useNavigate()
  const qc  = useQueryClient()

  const { data: existing } = useQuery({
    queryKey: ['connection', pid, id],
    queryFn: () => getConnection(pid!, id!),
    enabled: isEdit,
  })

  const [fields,  setFields]  = useState<Fields>(DEFAULTS)
  const [saving,  setSaving]  = useState(false)
  const [testing, setTesting] = useState(false)
  const [testMsg, setTestMsg] = useState<{ ok: boolean; text: string } | null>(null)
  const [error,   setError]   = useState<string | null>(null)

  useEffect(() => {
    if (existing) {
      setFields({
        ...DEFAULTS,
        name:       existing.name,
        sourceType: (existing.sourceType as 'database' | 'api') ?? 'database',
        // DB
        dbType:      existing.dbType || 'postgresql',
        host:        existing.host,
        port:        existing.port,
        database:    existing.database,
        username:    existing.username,
        password:    '',   // never pre-fill password
        extraOptions: existing.extraOptions,
        // API
        apiBaseUrl:       existing.apiBaseUrl,
        apiAuthType:      existing.apiAuthType || 'none',
        apiAuthValue:     '',  // never pre-fill secret
        apiDefaultHeaders: existing.apiDefaultHeaders,
      })
    }
  }, [existing])

  function set<K extends keyof Fields>(k: K, v: Fields[K]) {
    setFields(f => {
      const next = { ...f, [k]: v }
      if (k === 'dbType' && typeof v === 'string') next.port = DB_PORTS[v] ?? 5432
      return next
    })
  }

  const isApi = fields.sourceType === 'api'

  async function save() {
    setSaving(true); setError(null)
    try {
      const body: Record<string, unknown> = {
        name:       fields.name,
        sourceType: fields.sourceType,
      }
      if (isApi) {
        body.apiBaseUrl        = fields.apiBaseUrl
        body.apiAuthType       = fields.apiAuthType
        body.apiDefaultHeaders = fields.apiDefaultHeaders
        if (fields.apiAuthValue) body.apiAuthValue = fields.apiAuthValue
      } else {
        body.dbType      = fields.dbType
        body.host        = fields.host
        body.port        = fields.port
        body.database    = fields.database
        body.username    = fields.username
        body.extraOptions = fields.extraOptions
        if (!isEdit || fields.password) body.password = fields.password
      }

      if (isEdit) await updateConnection(pid!, id!, body)
      else        await createConnection(pid!, body)
      qc.invalidateQueries({ queryKey: ['connections', pid] })
      nav(`/providers/${pid}/connections`)
    } catch (e) { setError(String(e)) }
    finally { setSaving(false) }
  }

  async function testConn() {
    if (!id) return
    setTesting(true); setTestMsg(null)
    try {
      const r = await testConnection(pid!, id)
      setTestMsg({ ok: r.ok, text: r.ok ? (r.message ?? 'OK') : (r.error ?? 'Failed') })
    } catch (e) { setTestMsg({ ok: false, text: String(e) }) }
    finally { setTesting(false) }
  }

  return (
    <div className="p-8 max-w-xl">
      <div className="text-xs text-slate-500 mb-2">
        <span onClick={() => nav(`/providers/${pid}/connections`)} className="hover:underline cursor-pointer">
          Connections
        </span> / {isEdit ? 'Edit' : 'New'}
      </div>
      <h1 className="text-xl font-bold mb-6">{isEdit ? 'Edit Connection' : 'New Connection'}</h1>

      <div className="bg-white border border-slate-200 rounded-xl p-6 space-y-4">

        {/* Name */}
        <Field label="Connection Name" required>
          <input className={INPUT} value={fields.name}
            onChange={e => set('name', e.target.value)} placeholder="Sales API" />
        </Field>

        {/* Source Type toggle */}
        <Field label="Source Type" required>
          <div className="flex rounded-lg border border-slate-300 overflow-hidden text-sm">
            {(['database', 'api'] as const).map(st => (
              <button key={st}
                disabled={isEdit}
                onClick={() => set('sourceType', st)}
                className={`flex-1 py-2 font-medium transition-colors ${
                  fields.sourceType === st
                    ? 'bg-blue-600 text-white'
                    : 'bg-white text-slate-600 hover:bg-slate-50'
                } ${isEdit ? 'opacity-60 cursor-not-allowed' : 'cursor-pointer'}`}>
                {st === 'database' ? '🗄 Database' : '🌐 HTTP API'}
              </button>
            ))}
          </div>
          {isEdit && <p className="text-xs text-slate-400 mt-1">Source type cannot be changed after creation</p>}
        </Field>

        {/* ── Database fields ─────────────────────────────────────────────── */}
        {!isApi && (
          <>
            <Field label="Database Type" required>
              <select className={INPUT} value={fields.dbType} onChange={e => set('dbType', e.target.value)}>
                <option value="postgresql">PostgreSQL</option>
                <option value="sqlserver">SQL Server</option>
                <option value="mysql">MySQL / MariaDB</option>
              </select>
            </Field>

            <div className="grid grid-cols-3 gap-3">
              <div className="col-span-2">
                <Field label="Host" required>
                  <input className={INPUT} value={fields.host}
                    onChange={e => set('host', e.target.value)} placeholder="db.internal" />
                </Field>
              </div>
              <Field label="Port" required>
                <input className={INPUT} type="number" value={fields.port}
                  onChange={e => set('port', Number(e.target.value))} />
              </Field>
            </div>

            <Field label="Database Name" required>
              <input className={INPUT} value={fields.database}
                onChange={e => set('database', e.target.value)} placeholder="my_database" />
            </Field>

            <div className="grid grid-cols-2 gap-3">
              <Field label="Username" required>
                <input className={INPUT} value={fields.username}
                  onChange={e => set('username', e.target.value)} />
              </Field>
              <Field label={isEdit ? 'Password (blank = keep)' : 'Password'} required={!isEdit}>
                <input className={INPUT} type="password" value={fields.password}
                  onChange={e => set('password', e.target.value)} />
              </Field>
            </div>
          </>
        )}

        {/* ── API fields ──────────────────────────────────────────────────── */}
        {isApi && (
          <>
            <Field label="Base URL" required>
              <input className={INPUT} value={fields.apiBaseUrl}
                onChange={e => set('apiBaseUrl', e.target.value)}
                placeholder="https://api.example.com" />
              <p className="text-xs text-slate-400 mt-1">
                Endpoint paths in operations will be appended to this URL
              </p>
            </Field>

            <Field label="Authentication" required>
              <select className={INPUT} value={fields.apiAuthType}
                onChange={e => set('apiAuthType', e.target.value)}>
                {Object.entries(AUTH_LABELS).map(([v, l]) => (
                  <option key={v} value={v}>{l}</option>
                ))}
              </select>
            </Field>

            {fields.apiAuthType !== 'none' && (
              <Field label={AUTH_VALUE_LABELS[fields.apiAuthType] ?? 'Auth Value'}
                required={!isEdit}>
                <input className={INPUT} type="password" value={fields.apiAuthValue}
                  onChange={e => set('apiAuthValue', e.target.value)}
                  placeholder={isEdit ? 'Leave blank to keep existing' : ''} />
              </Field>
            )}

            <Field label="Default Headers (JSON)">
              <textarea className={`${INPUT} h-20 font-mono text-xs`}
                value={fields.apiDefaultHeaders}
                onChange={e => set('apiDefaultHeaders', e.target.value)}
                placeholder='{"X-Tenant": "acme", "Accept": "application/json"}' />
              <p className="text-xs text-slate-400 mt-1">
                These headers are sent with every request from this connection
              </p>
            </Field>
          </>
        )}

        {error   && <div className="text-sm text-red-600 bg-red-50 rounded px-3 py-2">{error}</div>}
        {testMsg && (
          <div className={`text-sm rounded px-3 py-2 ${testMsg.ok ? 'bg-green-50 text-green-700' : 'bg-red-50 text-red-600'}`}>
            {testMsg.text}
          </div>
        )}

        <div className="flex gap-3 pt-2">
          <button onClick={save} disabled={saving}
            className="px-5 py-2 bg-blue-600 text-white rounded text-sm font-medium hover:bg-blue-700 disabled:opacity-50">
            {saving ? 'Saving…' : isEdit ? 'Save' : 'Create'}
          </button>
          {isEdit && (
            <button onClick={testConn} disabled={testing}
              className="px-4 py-2 bg-slate-100 text-slate-700 rounded text-sm hover:bg-slate-200 disabled:opacity-50">
              {testing ? 'Testing…' : 'Test Connection'}
            </button>
          )}
          <button onClick={() => nav(`/providers/${pid}/connections`)}
            className="px-4 py-2 text-slate-600 text-sm hover:underline">Cancel</button>
        </div>
      </div>
    </div>
  )
}

const INPUT = 'w-full border border-slate-300 rounded px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500'

function Field({ label, children, required }: { label: string; children: React.ReactNode; required?: boolean }) {
  return (
    <div>
      <label className="block text-sm font-medium text-slate-700 mb-1">
        {label}{required && <span className="text-red-500 ml-1">*</span>}
      </label>
      {children}
    </div>
  )
}
