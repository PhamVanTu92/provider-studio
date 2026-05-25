import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { listProviders, startProvider, stopProvider, restartProvider } from '../api/providers'

const STATE_COLOR: Record<string, string> = {
  Connected:  'bg-green-500',
  Connecting: 'bg-yellow-400 animate-pulse',
  Backoff:    'bg-yellow-500 animate-pulse',
  Error:      'bg-red-500',
  Stopped:    'bg-slate-400',
}

export default function Dashboard() {
  const { data = [], refetch } = useQuery({
    queryKey: ['providers'],
    queryFn:  listProviders,
    refetchInterval: 5_000,
  })

  async function act(fn: () => Promise<unknown>) {
    try { await fn(); refetch() } catch (e) { alert(String(e)) }
  }

  return (
    <div className="p-8">
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-xl font-bold text-slate-800">Dashboard</h1>
        <Link to="/providers/new"
          className="px-4 py-2 rounded bg-blue-600 text-white text-sm font-medium hover:bg-blue-700">
          + New Provider
        </Link>
      </div>

      {data.length === 0 && (
        <div className="text-center py-20 text-slate-400">
          <div className="text-4xl mb-3">🔌</div>
          <div className="text-lg font-medium">No providers yet</div>
          <div className="text-sm mt-1">Create your first provider to get started</div>
        </div>
      )}

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {data.map(({ provider: p, status: s }) => (
          <div key={p.id} className="bg-white rounded-xl border border-slate-200 shadow-sm p-5 flex flex-col gap-4">
            {/* Header */}
            <div className="flex items-start justify-between gap-2">
              <div>
                <div className="font-semibold text-slate-800">{p.name}</div>
                <div className="text-xs text-slate-400 font-mono mt-0.5">{p.clientId}</div>
              </div>
              <span className={`h-2.5 w-2.5 mt-1.5 rounded-full shrink-0 ${STATE_COLOR[s?.state ?? 'Stopped']}`} />
            </div>

            {/* State */}
            <div className="flex flex-wrap gap-3 text-sm">
              <Stat label="State"   value={s?.state ?? 'Stopped'} />
              <Stat label="Handled" value={String(s?.operationsHandled ?? 0)} />
              <Stat label="Inflight" value={String(s?.inflightRequests ?? 0)} />
            </div>

            {s?.lastError && (
              <div className="text-xs text-red-600 bg-red-50 rounded px-2 py-1 truncate" title={s.lastError}>
                {s.lastError}
              </div>
            )}

            {/* Actions */}
            <div className="flex flex-wrap gap-2 pt-1 border-t border-slate-100">
              <Link to={`/providers/${p.id}/operations`}
                className="text-xs px-2.5 py-1 rounded bg-slate-100 hover:bg-slate-200 text-slate-700">
                Operations
              </Link>
              <Link to={`/providers/${p.id}/connections`}
                className="text-xs px-2.5 py-1 rounded bg-slate-100 hover:bg-slate-200 text-slate-700">
                DB Connections
              </Link>
              <Link to={`/providers/${p.id}/edit`}
                className="text-xs px-2.5 py-1 rounded bg-slate-100 hover:bg-slate-200 text-slate-700">
                Edit
              </Link>
              <div className="flex-1" />
              {s?.state === 'Connected' || s?.state === 'Connecting' ? (
                <>
                  <button onClick={() => act(() => restartProvider(p.id))}
                    className="text-xs px-2.5 py-1 rounded bg-yellow-100 hover:bg-yellow-200 text-yellow-800">
                    Restart
                  </button>
                  <button onClick={() => act(() => stopProvider(p.id))}
                    className="text-xs px-2.5 py-1 rounded bg-red-100 hover:bg-red-200 text-red-700">
                    Stop
                  </button>
                </>
              ) : (
                <button onClick={() => act(() => startProvider(p.id))}
                  className="text-xs px-2.5 py-1 rounded bg-green-100 hover:bg-green-200 text-green-800">
                  Start
                </button>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col">
      <span className="text-xs text-slate-400">{label}</span>
      <span className="font-medium text-slate-700">{value}</span>
    </div>
  )
}
