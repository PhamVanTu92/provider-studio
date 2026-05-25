import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { listOperations, deleteOperation } from '../api/operations'

const MODE_BADGE = { get: 'bg-blue-100 text-blue-700', push: 'bg-purple-100 text-purple-700' }
const TYPE_BADGE: Record<string, string> = {
  view: 'bg-slate-100 text-slate-600', storedproc: 'bg-orange-100 text-orange-700',
  function: 'bg-teal-100 text-teal-700', rawsql: 'bg-yellow-100 text-yellow-700',
}

export default function OperationList() {
  const { pid } = useParams<{ pid: string }>()
  const qc = useQueryClient()
  const { data = [] } = useQuery({ queryKey: ['operations', pid], queryFn: () => listOperations(pid!) })
  const del = useMutation({
    mutationFn: (id: string) => deleteOperation(pid!, id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['operations', pid] }),
  })

  return (
    <div className="p-8">
      <div className="flex items-center justify-between mb-6">
        <div>
          <div className="text-xs text-slate-500 mb-1">
            <Link to="/providers" className="hover:underline">Providers</Link> / Operations
          </div>
          <h1 className="text-xl font-bold">Operations</h1>
        </div>
        <Link to={`/providers/${pid}/operations/new`}
          className="px-4 py-2 bg-blue-600 text-white text-sm rounded hover:bg-blue-700">
          + New Operation
        </Link>
      </div>

      <div className="bg-white border border-slate-200 rounded-xl overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-slate-50 border-b border-slate-200">
            <tr>
              {['Pattern','Mode','Query Type','Enabled','Params','Actions'].map(h => (
                <th key={h} className="text-left px-4 py-3 font-medium text-slate-600">{h}</th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {data.map(op => (
              <tr key={op.id} className="hover:bg-slate-50">
                <td className="px-4 py-3 font-mono text-xs font-semibold text-slate-700">{op.pattern}</td>
                <td className="px-4 py-3">
                  <span className={`px-2 py-0.5 rounded text-xs font-medium ${MODE_BADGE[op.mode] ?? ''}`}>
                    {op.mode.toUpperCase()}
                  </span>
                </td>
                <td className="px-4 py-3">
                  <span className={`px-2 py-0.5 rounded text-xs font-medium ${TYPE_BADGE[op.queryType] ?? ''}`}>
                    {op.queryType}
                  </span>
                </td>
                <td className="px-4 py-3">
                  <span className={`px-2 py-0.5 rounded text-xs ${op.isEnabled ? 'bg-green-100 text-green-700' : 'bg-slate-100 text-slate-400'}`}>
                    {op.isEnabled ? 'Yes' : 'No'}
                  </span>
                </td>
                <td className="px-4 py-3 text-xs text-slate-500">{op.paramMappings.length} params</td>
                <td className="px-4 py-3">
                  <div className="flex gap-2">
                    <Link to={`/providers/${pid}/operations/${op.id}/edit`} className="text-blue-600 hover:underline text-xs">Edit</Link>
                    <button onClick={() => { if (confirm('Delete operation?')) del.mutate(op.id) }}
                      className="text-red-500 hover:underline text-xs">Delete</button>
                  </div>
                </td>
              </tr>
            ))}
            {data.length === 0 && (
              <tr><td colSpan={6} className="px-4 py-8 text-center text-slate-400">No operations yet</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}
