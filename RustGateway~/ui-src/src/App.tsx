import { useState } from 'react'
import 'reactflow/dist/style.css'
import 'xterm/css/xterm.css'
import './App.css'
import './components/dashboard/dashboard.css'
import { AITerminal } from './components/AITerminal'
import { NodeEditor } from './components/NodeEditor'
import type { ConnectionState, LuxEventEnvelope, ViewMode } from './types'
import { SessionManager } from './components/SessionManager'
import { RemoteViewer } from './components/RemoteViewer'
import { AITimeline } from './components/AITimeline'
import { DashboardLayout } from './components/dashboard/DashboardLayout'

function App() {
  const [activeView, setActiveView] = useState<ViewMode>('dashboard')
  const [events, setEvents] = useState<LuxEventEnvelope[]>([])
  const [connectionState, setConnectionState] = useState<ConnectionState>('idle')
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null)
  const latestEvent = events[0]

  if (activeView === 'dashboard') {
    return (
      <main className="app-shell" style={{ padding: 0, width: '100vw', height: '100vh', maxWidth: 'none' }}>
        <DashboardLayout />
        <div style={{ position: 'absolute', bottom: '16px', right: '16px', zIndex: 100 }}>
          <button onClick={() => setActiveView('nodes')} style={{ background: 'rgba(0,0,0,0.5)', border: '1px solid var(--line)' }}>
            Exit Dashboard
          </button>
        </div>
      </main>
    )
  }

  return (
    <main className="app-shell">
      <header className="app-header">
        <div>
          <p className="eyebrow">LUX Gateway</p>
          <h1>Pipeline console</h1>
        </div>
        <div className={`status-pill status-pill--${connectionState}`}>
          <span />
          {connectionState}
        </div>
      </header>

      <nav className="view-tabs" aria-label="Lux workspace views">
        <button className="" onClick={() => setActiveView('dashboard')}>
          Dashboard
        </button>
        <button className={activeView === 'nodes' ? 'active' : ''} onClick={() => setActiveView('nodes')}>
          Node editor
        </button>
        <button className={activeView === 'terminal' ? 'active' : ''} onClick={() => setActiveView('terminal')}>
          AI terminal
        </button>
        <button className={activeView === 'remote' ? 'active' : ''} onClick={() => setActiveView('remote')}>
          Remote
        </button>
        <button className={activeView === 'timeline' ? 'active' : ''} onClick={() => setActiveView('timeline')}>
          Timeline
        </button>
      </nav>

      <section className="workspace-card">
        {activeView === 'nodes' && (
          <NodeEditor latestEvent={latestEvent} />
        )}
        {activeView === 'terminal' && (
          <AITerminal onEvent={setEvents} onConnectionState={setConnectionState} />
        )}
        {activeView === 'remote' && (
          activeSessionId ? (
            <RemoteViewer 
              sessionId={activeSessionId} 
              onDisconnect={() => setActiveSessionId(null)} 
            />
          ) : (
            <SessionManager onSessionSelect={setActiveSessionId} />
          )
        )}
        {activeView === 'timeline' && (
          <AITimeline />
        )}
      </section>
    </main>
  )
}

export default App
