function getGatewayToken(): string {
  return localStorage.getItem('lux-gateway-token') || ''
}

function authHeaders(): HeadersInit {
  const token = getGatewayToken()
  return token ? { 'x-lux-token': token } : {}
}
import { useState, useEffect } from 'react'
import type { RemoteSession } from '../types'

interface SessionManagerProps {
  onSessionSelect: (sessionId: string) => void
}

export function SessionManager({ onSessionSelect }: SessionManagerProps) {
  const [sessions, setSessions] = useState<RemoteSession[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchSessions = async () => {
    try {
      setLoading(true)
      setError(null)
      const response = await fetch('/api/remote/sessions', { headers: authHeaders() })
      if (!response.ok) {
        throw new Error('Failed to fetch sessions')
      }
      const data = await response.json()
      setSessions(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    fetchSessions()
  }, [])

  const handleCreateSession = async () => {
    try {
      setLoading(true)
      setError(null)
      const response = await fetch('/api/remote/sessions', {
        method: 'POST',
        headers: authHeaders()
      })
      if (!response.ok) {
        throw new Error('Failed to create session')
      }
      const newSession: RemoteSession = await response.json()
      setSessions([...sessions, newSession])
      onSessionSelect(newSession.id)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="session-manager">
      <div className="session-manager-header">
        <h2>Remote Sessions</h2>
        <button onClick={handleCreateSession} disabled={loading} className="execute-button">
          Create New Session
        </button>
      </div>
      
      {error && <div className="error-message">{error}</div>}
      
      <div className="session-list">
        {sessions.length === 0 && !loading && (
          <p className="empty-state">No active sessions found.</p>
        )}
        
        {sessions.map(session => (
          <div key={session.id} className="session-item">
            <div className="session-info">
              <strong>Session ID:</strong> {session.id.substring(0, 8)}...
              <span className={`status-badge status-${session.status}`}>
                {session.status}
              </span>
            </div>
            <div className="session-details">
              <span>Created: {new Date(session.createdAtUtc).toLocaleString()}</span>
            </div>
            <button 
              onClick={() => onSessionSelect(session.id)}
              className="connect-button"
            >
              Connect
            </button>
          </div>
        ))}
      </div>
    </div>
  )
}
