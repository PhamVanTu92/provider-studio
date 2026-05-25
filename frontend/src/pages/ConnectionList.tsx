import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { listConnections, deleteConnection, testConnection } from '../api/connections'

const DB_BADGE: Record<string, string> = {
  postgresql: 'bg-blue-100 text-blue-700',
  sqlserver:  'bg-orange-100 text-orange-700',
  mysql:      'bg-yellow-100 text-yellow-700',
}

export default function ConnectionList() {
  const { pid } = useParams<{ pid: string }>()
  const qc = useQueryClient()
  const { data = [] } = useQuery({ queryKey: ['connections', pid], queryFn: () => listConnections(pid!) })
  const del = useMutation({
    mutationFn: (id: string) => deleteConnection(pid!, id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['connections', pid] }),
  })

  async function test(id: string) {
    const r = await testConnection(pid!, id)
    alert(r.ok ? `✅ ${r.message}` : `❌ ${r.error}`)
  }

  return (
    <div className="p-8">
      <div className="flex items-center justify-between mb-6">
        <div>
          <div className="text-xs text-slate-500 mb-1">
            <Link to="/providers" className="hover:underline">Providers</Link> / DB Connections
          </div>
          <h1 className="text-xl font-bold">DB Connections</h1>
        </div>
        <Link to={`/providers/${pid}/connections/new`}
          className="px-4 py-2 bg-blue-600 text-white text-sm rounded hover:bg-blue-700">
          + New Connection
        </Link>
      </div>

      <div className="bg-white border border-slate-200 rounded-xl overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-slate-50 border-b border-slate-200">
            <tr>
              {['Name','Type','Host','Database','Actions'].map(h => (
                <th key={h} className="text-left px-4 py-3 font-medium text-slate-600">{h}</th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {data.map(c => (
              <tr key={c.id} className="hover:bg-slate-50">
                <td className="px-4 py-3 font-medium">{c.name}</td>
                <td className="px-4 py-3">
                  <span className={`px-2 py-0.5 rounded text-xs font-medium ${DB_BADGE[c.dbType] ?? 'bg-slate-100 text-slate-600'}`}>
                    {c.dbType}
                  </span>
                </td>
                <td className="px-4 py-3 text-xs text-slate-500">{c.host}:{c.port}</td>
                <td className="px-4 py-3 text-xs text-slate-500">{c.database}</td>
                <td className="px-4 py-3">
                  <div className="flex gap-2">
                    <button onClick={() => test(c.id)} className="text-green-600 hover:underline text-xs">Test</button>
                    <Link to={`/providers/${pid}/connections/${c.id}/edit`} className="text-blue-600 hover:underline text-xs">Edit</Link>
                    <button onClick={() => { if (confirm('Delete connection?')) del.mutate(c.id) }}
                      className="text-red-500 hover:underline text-xs">Delete</button>
                  </div>
                </td>
              </tr>
            ))}
            {data.length === 0 && (
              <tr><td colSpan={5} className="px-4 py-8 text-center text-slate-400">No connections yet</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}
