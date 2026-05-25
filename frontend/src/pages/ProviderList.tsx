import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { listProviders, deleteProvider } from '../api/providers'

export default function ProviderList() {
  const qc = useQueryClient()
  const { data = [] } = useQuery({ queryKey: ['providers'], queryFn: listProviders })
  const del = useMutation({
    mutationFn: (id: string) => deleteProvider(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['providers'] }),
  })

  return (
    <div className="p-8">
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-xl font-bold">Providers</h1>
        <Link to="/providers/new"
          className="px-4 py-2 bg-blue-600 text-white text-sm rounded hover:bg-blue-700">
          + New Provider
        </Link>
      </div>

      <div className="bg-white border border-slate-200 rounded-xl overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-slate-50 border-b border-slate-200">
            <tr>
              {['Name','Client ID','Bridge URL','Version','Enabled','Actions'].map(h => (
                <th key={h} className="text-left px-4 py-3 font-medium text-slate-600">{h}</th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {data.map(({ provider: p }) => (
              <tr key={p.id} className="hover:bg-slate-50">
                <td className="px-4 py-3 font-medium">{p.name}</td>
                <td className="px-4 py-3 font-mono text-xs text-slate-500">{p.clientId}</td>
                <td className="px-4 py-3 text-xs text-slate-500 truncate max-w-xs">{p.bridgeGrpcUrl}</td>
                <td className="px-4 py-3 text-xs">{p.version}</td>
                <td className="px-4 py-3">
                  <span className={`inline-block px-2 py-0.5 rounded text-xs font-medium ${
                    p.isEnabled ? 'bg-green-100 text-green-700' : 'bg-slate-100 text-slate-500'}`}>
                    {p.isEnabled ? 'Yes' : 'No'}
                  </span>
                </td>
                <td className="px-4 py-3">
                  <div className="flex gap-2">
                    <Link to={`/providers/${p.id}/edit`}
                      className="text-blue-600 hover:underline text-xs">Edit</Link>
                    <Link to={`/providers/${p.id}/connections`}
                      className="text-slate-600 hover:underline text-xs">Connections</Link>
                    <Link to={`/providers/${p.id}/operations`}
                      className="text-slate-600 hover:underline text-xs">Operations</Link>
                    <button onClick={() => { if (confirm('Delete provider?')) del.mutate(p.id) }}
                      className="text-red-500 hover:underline text-xs">Delete</button>
                  </div>
                </td>
              </tr>
            ))}
            {data.length === 0 && (
              <tr><td colSpan={6} className="px-4 py-8 text-center text-slate-400">No providers</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}
