import { Navigate, Route, Routes } from 'react-router-dom'
import { useSession } from './api/session'
import { Layout } from './components/Layout'
import { ApiKeysPage } from './pages/ApiKeysPage'
import { ConnectPage } from './pages/ConnectPage'
import { DeliveriesPage } from './pages/DeliveriesPage'
import { EndpointsPage } from './pages/EndpointsPage'
import { EventsPage } from './pages/EventsPage'
import { OverviewPage } from './pages/OverviewPage'

export function App() {
  const { settings } = useSession()

  if (!settings) return <ConnectPage />

  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<OverviewPage />} />
        <Route path="endpoints" element={<EndpointsPage />} />
        <Route path="events" element={<EventsPage />} />
        <Route path="deliveries" element={<DeliveriesPage />} />
        <Route path="api-keys" element={<ApiKeysPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  )
}
