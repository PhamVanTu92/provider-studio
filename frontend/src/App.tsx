import { BrowserRouter, Routes, Route, NavLink } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import Dashboard       from './pages/Dashboard'
import ProviderList    from './pages/ProviderList'
import ProviderForm    from './pages/ProviderForm'
import ConnectionList  from './pages/ConnectionList'
import ConnectionForm  from './pages/ConnectionForm'
import OperationList   from './pages/OperationList'
import OperationForm   from './pages/OperationForm'

const qc = new QueryClient({ defaultOptions: { queries: { staleTime: 10_000 } } })

const NAV = [
  { to: '/',            label: '⬡ Dashboard' },
  { to: '/providers',   label: '🔌 Providers' },
]

export default function App() {
  return (
    <QueryClientProvider client={qc}>
      <BrowserRouter>
        <div className="flex min-h-screen bg-slate-50 text-slate-800">
          {/* Sidebar */}
          <aside className="w-52 shrink-0 bg-slate-900 text-slate-100 flex flex-col">
            <div className="px-5 py-4 border-b border-slate-700">
              <div className="text-sm font-bold text-white">Provider Studio</div>
              <div className="text-xs text-slate-400 mt-0.5">HDOS no-code builder</div>
            </div>
            <nav className="flex-1 py-3 space-y-0.5 px-2">
              {NAV.map(n => (
                <NavLink key={n.to} to={n.to} end={n.to === '/'}
                  className={({ isActive }) =>
                    `flex items-center gap-2 px-3 py-2 rounded text-sm transition-colors ${
                      isActive ? 'bg-blue-600 text-white' : 'text-slate-300 hover:bg-slate-700'}`}>
                  {n.label}
                </NavLink>
              ))}
            </nav>
            <div className="px-4 py-3 border-t border-slate-700 text-xs text-slate-500">
              v1.0.0
            </div>
          </aside>

          {/* Main */}
          <main className="flex-1 overflow-auto">
            <Routes>
              <Route path="/"                                  element={<Dashboard />} />
              <Route path="/providers"                         element={<ProviderList />} />
              <Route path="/providers/new"                     element={<ProviderForm />} />
              <Route path="/providers/:pid/edit"               element={<ProviderForm />} />
              <Route path="/providers/:pid/connections"        element={<ConnectionList />} />
              <Route path="/providers/:pid/connections/new"    element={<ConnectionForm />} />
              <Route path="/providers/:pid/connections/:id/edit" element={<ConnectionForm />} />
              <Route path="/providers/:pid/operations"         element={<OperationList />} />
              <Route path="/providers/:pid/operations/new"     element={<OperationForm />} />
              <Route path="/providers/:pid/operations/:id/edit" element={<OperationForm />} />
            </Routes>
          </main>
        </div>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
