import { useState, useEffect } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { getOperation, createOperation, updateOperation, testOperation } from '../api/operations'
import { listConnections } from '../api/connections'

// ─── Types ────────────────────────────────────────────────────────────────────

interface ParamRow {
  jsonPath: string; paramName: string; paramType: string
  isRequired: boolean; defaultValue: string; sortOrder: number
  apiTarget: string
}

interface Fields {
  dbConnectionId: string; pattern: string; mode: 'get' | 'push'
  queryType: string; queryTarget: string
  pushPollIntervalSeconds: number; pushChangeQuery: string
  isEnabled: boolean; paramMappings: ParamRow[]
}

const DEFAULTS: Fields = {
  dbConnectionId: '', pattern: '', mode: 'get',
  queryType: 'rawsql', queryTarget: '',
  pushPollIntervalSeconds: 60, pushChangeQuery: '',
  isEnabled: true, paramMappings: [],
}

const PARAM_TYPES = ['string', 'int', 'decimal', 'date', 'bool']

// DB query types
const DB_QUERY_TYPES = [
  { value: 'view',       label: 'View' },
  { value: 'storedproc', label: 'Stored Procedure' },
  { value: 'function',   label: 'Table Function' },
  { value: 'rawsql',     label: 'Raw SQL' },
]

// API query types (maps to HTTP method on backend)
const API_QUERY_TYPES = [
  { value: 'view',    label: 'GET' },
  { value: 'rawsql',  label: 'POST (JSON body)' },
]

const API_TARGETS = ['query', 'body', 'path', 'header']

// ─── Component ────────────────────────────────────────────────────────────────

export default function OperationForm() {
  const { pid, id } = useParams<{ pid: string; id: string }>()
  const isEdit = !!id
  const nav = useNavigate()
  const qc  = useQueryClient()

  const { data: conns = [] }  = useQuery({ queryKey: ['connections', pid], queryFn: () => listConnections(pid!) })
  const { data: existing }    = useQuery({
    queryKey: ['operation', pid, id],
    queryFn: () => getOperation(pid!, id!),
    enabled: isEdit,
  })

  const [fields,     setFields]     = useState<Fields>(DEFAULTS)
  const [saving,     setSaving]     = useState(false)
  const [testJson,   setTestJson]   = useState('{}')
  const [testResult, setTestResult] = useState<{ ok: boolean; ms?: number; data?: object; error?: string } | null>(null)
  const [testing,    setTesting]    = useState(false)
  const [error,      setError]      = useState<string | null>(null)

  useEffect(() => {
    if (existing) setFields({
      ...DEFAULTS,
      ...existing,
      paramMappings: existing.paramMappings.map(p => ({
        jsonPath: p.jsonPath, paramName: p.paramName, paramType: p.paramType,
        isRequired: p.isRequired, defaultValue: p.defaultValue, sortOrder: p.sortOrder,
        apiTarget: (p as any).apiTarget ?? 'query',
      })),
    })
  }, [existing])

  useEffect(() => {
    if (!isEdit && conns.length > 0 && !fields.dbConnectionId)
      setFields(f => ({ ...f, dbConnectionId: conns[0].id }))
  }, [conns, isEdit, fields.dbConnectionId])

  // Determine if the selected connection is an API source
  const selectedConn = conns.find(c => c.id === fields.dbConnectionId)
  const isApiConn    = selectedConn?.sourceType === 'api'

  function setF<K extends keyof Fields>(k: K, v: Fields[K]) {
    setFields(f => ({ ...f, [k]: v }))
  }

  // ── Param mappings ────────────────────────────────────────────────────────

  function addParam() {
    setFields(f => ({
      ...f, paramMappings: [...f.paramMappings, {
        jsonPath: '', paramName: '', paramType: 'string',
        isRequired: false, defaultValue: '', sortOrder: f.paramMappings.length,
        apiTarget: 'query',
      }]
    }))
  }

  function removeParam(i: number) {
    setFields(f => ({ ...f, paramMappings: f.paramMappings.filter((_, j) => j !== i) }))
  }

  function setParam(i: number, k: keyof ParamRow, v: string | boolean | number) {
    setFields(f => {
      const rows = [...f.paramMappings]
      rows[i] = { ...rows[i], [k]: v }
      return { ...f, paramMappings: rows }
    })
  }

  // ── Save ──────────────────────────────────────────────────────────────────

  async function save() {
    setSaving(true); setError(null)
    try {
      const body = { ...fields }
      if (isEdit) await updateOperation(pid!, id!, body)
      else        await createOperation(pid!, body)
      qc.invalidateQueries({ queryKey: ['operations', pid] })
      nav(`/providers/${pid}/operations`)
    } catch (e) { setError(String(e)) }
    finally { setSaving(false) }
  }

  // ── Test ──────────────────────────────────────────────────────────────────

  async function runTest() {
    if (!id) return
    setTesting(true); setTestResult(null)
    try {
      const r = await testOperation(pid!, id, testJson)
      setTestResult({
        ok: r.ok, ms: r.elapsedMs,
        data: r.result ? JSON.parse(r.result) as object : undefined,
        error: r.error,
      })
    } catch (e) { setTestResult({ ok: false, error: String(e) }) }
    finally { setTesting(false) }
  }

  const queryTypes = isApiConn ? API_QUERY_TYPES : DB_QUERY_TYPES

  return (
    <div className="p-8 max-w-3xl">
      <div className="text-xs text-slate-500 mb-2">
        <span onClick={() => nav(`/providers/${pid}/operations`)} className="hover:underline cursor-pointer">
          Operations
        </span> / {isEdit ? 'Edit' : 'New'}
      </div>
      <h1 className="text-xl font-bold mb-6">Operation Builder</h1>

      <div className="bg-white border border-slate-200 rounded-xl p-6 space-y-5">

        {/* Basic */}
        <div className="grid grid-cols-2 gap-4">
          <Field label="Operation Pattern" required>
            <input className={INPUT} value={fields.pattern}
              onChange={e => setF('pattern', e.target.value)}
              placeholder="report.sales.daily" />
            <p className="text-xs text-slate-400 mt-1">Phải khớp với pattern đăng ký trên HDOS</p>
          </Field>
          <Field label="Connection" required>
            <select className={INPUT} value={fields.dbConnectionId}
              onChange={e => setF('dbConnectionId', e.target.value)}>
              {conns.map(c => (
                <option key={c.id} value={c.id}>
                  {c.name} ({c.sourceType === 'api' ? '🌐 API' : `🗄 ${c.dbType}`})
                </option>
              ))}
            </select>
          </Field>
        </div>

        {/* API source info banner */}
        {isApiConn && selectedConn && (
          <div className="bg-blue-50 border border-blue-200 rounded-lg px-4 py-2 text-xs text-blue-700">
            <strong>API Source:</strong> {selectedConn.apiBaseUrl || '(no base URL set)'}
            {' · '}Auth: {selectedConn.apiAuthType}
          </div>
        )}

        {/* Mode */}
        <Field label="Mode" required>
          <div className="flex gap-4 mt-1">
            {(['get', 'push'] as const).map(m => (
              <label key={m} className="flex items-center gap-2 cursor-pointer">
                <input type="radio" name="mode" value={m} checked={fields.mode === m}
                  onChange={() => setF('mode', m)} className="accent-blue-600" />
                <div>
                  <div className="text-sm font-medium">{m === 'get' ? 'GET' : 'PUSH'}</div>
                  <div className="text-xs text-slate-400">
                    {m === 'get' ? 'Trả lời khi HDOS query' : 'Tự push khi data thay đổi'}
                  </div>
                </div>
              </label>
            ))}
          </div>
        </Field>

        {/* Query / Request */}
        <div className="grid grid-cols-3 gap-4">
          <Field label={isApiConn ? 'HTTP Method' : 'Query Type'} required>
            <select className={INPUT} value={fields.queryType}
              onChange={e => setF('queryType', e.target.value)}>
              {queryTypes.map(q => <option key={q.value} value={q.value}>{q.label}</option>)}
            </select>
          </Field>
          <div className="col-span-2">
            <Field label={isApiConn ? 'Endpoint Path' : (fields.queryType === 'rawsql' ? 'SQL Query' : 'View / Proc / Function')} required>
              {!isApiConn && fields.queryType === 'rawsql' ? (
                <textarea className={`${INPUT} h-24 font-mono text-xs`}
                  value={fields.queryTarget}
                  onChange={e => setF('queryTarget', e.target.value)}
                  placeholder="SELECT * FROM sales WHERE sale_date = @date" />
              ) : (
                <input className={INPUT} value={fields.queryTarget}
                  onChange={e => setF('queryTarget', e.target.value)}
                  placeholder={isApiConn
                    ? '/api/v1/report/daily  or  /api/v1/report/{id}'
                    : (fields.queryType === 'view' ? 'vw_daily_sales' : 'sp_daily_sales')} />
              )}
              {isApiConn && (
                <p className="text-xs text-slate-400 mt-1">
                  Use {'{'}<em>param</em>{'}'} for path params, e.g. <code>/orders/{'{'}<em>orderId</em>{'}'}</code>
                </p>
              )}
            </Field>
          </div>
        </div>

        {/* Push settings */}
        {fields.mode === 'push' && (
          <div className="border border-purple-200 bg-purple-50 rounded-lg p-4 space-y-3">
            <div className="text-xs font-semibold text-purple-700 uppercase tracking-wide">Push Settings</div>
            <div className="grid grid-cols-2 gap-4">
              <Field label="Poll Interval (seconds)">
                <input className={INPUT} type="number" min={10}
                  value={fields.pushPollIntervalSeconds}
                  onChange={e => setF('pushPollIntervalSeconds', Number(e.target.value))} />
              </Field>
              <Field label={isApiConn ? 'Change Detection Endpoint' : 'Change Detection Query'}>
                <input className={INPUT} value={fields.pushChangeQuery}
                  onChange={e => setF('pushChangeQuery', e.target.value)}
                  placeholder={isApiConn
                    ? '/api/v1/report/last-updated'
                    : 'SELECT MAX(updated_at) FROM sales'} />
                <p className="text-xs text-slate-400 mt-1">Để trống = push theo interval (unconditional)</p>
              </Field>
            </div>
          </div>
        )}

        {/* Param mappings */}
        <div className="border-t border-slate-100 pt-4">
          <div className="flex items-center justify-between mb-3">
            <div className="text-sm font-semibold text-slate-700">Parameter Mappings</div>
            <button onClick={addParam}
              className="text-xs px-3 py-1 bg-slate-100 hover:bg-slate-200 rounded text-slate-700">
              + Add Param
            </button>
          </div>

          {fields.paramMappings.length > 0 && (
            <div className="overflow-x-auto">
              <table className="w-full text-xs border border-slate-200 rounded-lg overflow-hidden">
                <thead className="bg-slate-50">
                  <tr>
                    {['JSON Key', isApiConn ? 'Param Name' : 'SQL Param', 'Type', 'Required', 'Default',
                      ...(isApiConn ? ['API Target'] : []), ''].map(h => (
                      <th key={h} className="text-left px-3 py-2 font-medium text-slate-600 border-b border-slate-200">{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {fields.paramMappings.map((p, i) => (
                    <tr key={i}>
                      <td className="px-2 py-1.5">
                        <input className={MINI_INPUT} value={p.jsonPath}
                          onChange={e => setParam(i, 'jsonPath', e.target.value)}
                          placeholder="fromDate" />
                      </td>
                      <td className="px-2 py-1.5">
                        <input className={MINI_INPUT} value={p.paramName}
                          onChange={e => setParam(i, 'paramName', e.target.value)}
                          placeholder={isApiConn ? 'from_date' : '@from_date'} />
                      </td>
                      <td className="px-2 py-1.5">
                        <select className={MINI_INPUT} value={p.paramType}
                          onChange={e => setParam(i, 'paramType', e.target.value)}>
                          {PARAM_TYPES.map(t => <option key={t}>{t}</option>)}
                        </select>
                      </td>
                      <td className="px-2 py-1.5 text-center">
                        <input type="checkbox" checked={p.isRequired}
                          onChange={e => setParam(i, 'isRequired', e.target.checked)}
                          className="accent-blue-600" />
                      </td>
                      <td className="px-2 py-1.5">
                        <input className={MINI_INPUT} value={p.defaultValue}
                          onChange={e => setParam(i, 'defaultValue', e.target.value)}
                          placeholder="today" />
                      </td>
                      {isApiConn && (
                        <td className="px-2 py-1.5">
                          <select className={MINI_INPUT} value={p.apiTarget}
                            onChange={e => setParam(i, 'apiTarget', e.target.value)}>
                            {API_TARGETS.map(t => <option key={t}>{t}</option>)}
                          </select>
                        </td>
                      )}
                      <td className="px-2 py-1.5">
                        <button onClick={() => removeParam(i)} className="text-red-400 hover:text-red-600">✕</button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {fields.paramMappings.length === 0 && (
            <div className="text-xs text-slate-400 text-center py-4 border border-dashed border-slate-200 rounded-lg">
              No params — click "+ Add Param" to add
            </div>
          )}

          {isApiConn && (
            <p className="text-xs text-slate-400 mt-2">
              <strong>API Target:</strong> <em>query</em> = URL query string · <em>body</em> = JSON body (POST) · <em>path</em> = URL path substitution · <em>header</em> = request header
            </p>
          )}
        </div>

        {/* Enabled */}
        <div className="flex items-center gap-2 border-t border-slate-100 pt-4">
          <input type="checkbox" id="enabled" checked={fields.isEnabled}
            onChange={e => setF('isEnabled', e.target.checked)}
            className="w-4 h-4 accent-blue-600" />
          <label htmlFor="enabled" className="text-sm text-slate-700">Enable this operation</label>
        </div>

        {error && <div className="text-sm text-red-600 bg-red-50 rounded px-3 py-2">{error}</div>}

        {/* Actions */}
        <div className="flex gap-3 pt-2">
          <button onClick={save} disabled={saving}
            className="px-5 py-2 bg-blue-600 text-white rounded text-sm font-medium hover:bg-blue-700 disabled:opacity-50">
            {saving ? 'Saving…' : isEdit ? 'Save Changes' : 'Create Operation'}
          </button>
          <button onClick={() => nav(`/providers/${pid}/operations`)}
            className="px-4 py-2 text-slate-600 text-sm hover:underline">Cancel</button>
        </div>
      </div>

      {/* Test panel (edit only) */}
      {isEdit && (
        <div className="mt-6 bg-white border border-slate-200 rounded-xl p-6">
          <div className="text-sm font-semibold mb-3">
            {isApiConn ? 'Test API Call' : 'Test Operation'}
          </div>
          <div className="mb-3">
            <label className="text-xs text-slate-500 mb-1 block">Params JSON</label>
            <textarea className={`${INPUT} font-mono text-xs h-20`}
              value={testJson} onChange={e => setTestJson(e.target.value)}
              placeholder={isApiConn
                ? '{"orderId":"123","status":"active"}'
                : '{"fromDate":"2026-01-01","toDate":"2026-01-31"}'} />
          </div>
          <button onClick={runTest} disabled={testing}
            className="px-4 py-2 bg-slate-700 text-white rounded text-sm hover:bg-slate-800 disabled:opacity-50">
            {testing ? 'Running…' : isApiConn ? '▶ Call API' : '▶ Run Query'}
          </button>

          {testResult && (
            <div className="mt-4">
              <div className={`text-xs font-semibold mb-2 ${testResult.ok ? 'text-green-600' : 'text-red-600'}`}>
                {testResult.ok ? `✅ OK — ${testResult.ms}ms` : '❌ Error'}
              </div>
              {testResult.error && <div className="text-xs text-red-600 mb-2">{testResult.error}</div>}
              {testResult.data && (
                <pre className="bg-slate-900 text-green-400 rounded p-3 text-xs overflow-auto max-h-60">
                  {JSON.stringify(testResult.data, null, 2)}
                </pre>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  )
}

const INPUT = 'w-full border border-slate-300 rounded px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500'
const MINI_INPUT = 'w-full border border-slate-200 rounded px-2 py-1 text-xs focus:outline-none focus:ring-1 focus:ring-blue-400'

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
