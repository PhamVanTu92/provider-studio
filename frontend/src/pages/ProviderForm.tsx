import { useState, useEffect } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { getProvider, createProvider, updateProvider, testAuth } from '../api/providers'

interface Fields {
  name: string; clientId: string; clientSecret: string
  tokenEndpoint: string; bridgeGrpcUrl: string
  ingestionBaseUrl: string; ingestionTokenEndpoint: string
  version: string; isEnabled: boolean
}

const DEFAULTS: Fields = {
  name: '', clientId: '', clientSecret: '',
  tokenEndpoint: 'http://hdos-host:5000/api/v1/providers/token',
  bridgeGrpcUrl: 'http://hdos-host:5400',
  ingestionBaseUrl: 'http://hdos-host:5100',
  ingestionTokenEndpoint: 'http://hdos-host:5000/api/v1/providers/token',
  version: '1.0.0', isEnabled: true,
}

export default function ProviderForm() {
  const { pid } = useParams<{ pid: string }>()
  const isEdit  = !!pid
  const nav     = useNavigate()
  const qc      = useQueryClient()

  const { data: existing } = useQuery({
    queryKey: ['provider', pid],
    queryFn:  () => getProvider(pid!),
    enabled:  isEdit,
  })

  const [fields, setFields] = useState<Fields>(DEFAULTS)
  const [saving,  setSaving]  = useState(false)
  const [testing, setTesting] = useState(false)
  const [testMsg, setTestMsg] = useState<{ ok: boolean; text: string } | null>(null)
  const [error,   setError]   = useState<string | null>(null)

  useEffect(() => {
    if (existing) setFields({ ...DEFAULTS, ...existing, clientSecret: '' })
  }, [existing])

  function set(k: keyof Fields, v: string | boolean) {
    setFields(f => ({ ...f, [k]: v }))
  }

  async function save() {
    setSaving(true); setError(null)
    try {
      const body: Record<string, unknown> = { ...fields }
      if (isEdit && !fields.clientSecret) delete body.clientSecret
      if (isEdit) await updateProvider(pid!, body)
      else        await createProvider(body)
      qc.invalidateQueries({ queryKey: ['providers'] })
      nav('/providers')
    } catch (e) { setError(String(e)) }
    finally { setSaving(false) }
  }

  async function testAuthentication() {
    if (!pid) return
    setTesting(true); setTestMsg(null)
    try {
      const r = await testAuth(pid)
      setTestMsg({ ok: r.ok, text: r.ok ? 'Authentication OK' : (r.error ?? 'Failed') })
    } catch (e) { setTestMsg({ ok: false, text: String(e) }) }
    finally { setTesting(false) }
  }

  return (
    <div className="p-8 max-w-2xl">
      <h1 className="text-xl font-bold mb-6">{isEdit ? 'Edit Provider' : 'New Provider'}</h1>

      <div className="bg-white border border-slate-200 rounded-xl p-6 space-y-4">
        <Field label="Provider Name" required>
          <input className={INPUT} value={fields.name} onChange={e => set('name', e.target.value)} placeholder="My Sales Provider" />
        </Field>

        <div className="border-t border-slate-100 pt-4">
          <div className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-3">HDOS Credentials</div>
          <div className="grid grid-cols-2 gap-4">
            <Field label="Client ID" required>
              <input className={INPUT} value={fields.clientId} onChange={e => set('clientId', e.target.value)}
                placeholder="my-provider" disabled={isEdit} />
            </Field>
            <Field label={isEdit ? 'Client Secret (leave blank to keep)' : 'Client Secret'} required={!isEdit}>
              <input className={INPUT} type="password" value={fields.clientSecret}
                onChange={e => set('clientSecret', e.target.value)} placeholder="••••••••" />
            </Field>
          </div>
        </div>

        <div className="border-t border-slate-100 pt-4">
          <div className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-3">HDOS Endpoints</div>
          <div className="space-y-3">
            {([
              ['tokenEndpoint', 'Token Endpoint'],
              ['bridgeGrpcUrl', 'Bridge gRPC URL'],
              ['ingestionBaseUrl', 'Ingestion Base URL'],
              ['ingestionTokenEndpoint', 'Ingestion Token Endpoint'],
            ] as [keyof Fields, string][]).map(([k, label]) => (
              <Field key={k} label={label} required>
                <input className={INPUT} value={fields[k] as string}
                  onChange={e => set(k, e.target.value)} />
              </Field>
            ))}
          </div>
        </div>

        <div className="border-t border-slate-100 pt-4 grid grid-cols-2 gap-4">
          <Field label="Version">
            <input className={INPUT} value={fields.version} onChange={e => set('version', e.target.value)} />
          </Field>
          <Field label="Enabled">
            <label className="flex items-center gap-2 mt-2 cursor-pointer">
              <input type="checkbox" checked={fields.isEnabled}
                onChange={e => set('isEnabled', e.target.checked)}
                className="w-4 h-4 accent-blue-600" />
              <span className="text-sm">Active</span>
            </label>
          </Field>
        </div>

        {error && <div className="text-sm text-red-600 bg-red-50 rounded px-3 py-2">{error}</div>}
        {testMsg && (
          <div className={`text-sm rounded px-3 py-2 ${testMsg.ok ? 'bg-green-50 text-green-700' : 'bg-red-50 text-red-600'}`}>
            {testMsg.text}
          </div>
        )}

        <div className="flex gap-3 pt-2">
          <button onClick={save} disabled={saving}
            className="px-5 py-2 bg-blue-600 text-white rounded text-sm font-medium hover:bg-blue-700 disabled:opacity-50">
            {saving ? 'Saving…' : isEdit ? 'Save Changes' : 'Create Provider'}
          </button>
          {isEdit && (
            <button onClick={testAuthentication} disabled={testing}
              className="px-5 py-2 bg-slate-100 text-slate-700 rounded text-sm hover:bg-slate-200 disabled:opacity-50">
              {testing ? 'Testing…' : 'Test Auth'}
            </button>
          )}
          <button onClick={() => nav('/providers')}
            className="px-5 py-2 text-slate-600 text-sm hover:underline">Cancel</button>
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
