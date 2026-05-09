import React, { useState, useEffect, useRef } from 'react';

interface LogEntry {
  id: number;
  timestamp: Date;
  level: 'info' | 'warning' | 'error';
  message: string;
}

export const LogPanel: React.FC = () => {
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const [levelFilter, setLevelFilter] = useState<'all' | 'info' | 'warning' | 'error'>('all');
  const [searchQuery, setSearchQuery] = useState('');
  const [autoScroll, setAutoScroll] = useState(true);
  const logEndRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const interval = setInterval(() => {
      const levels: ('info' | 'warning' | 'error')[] = ['info', 'info', 'info', 'warning', 'error'];
      const randomLevel = levels[Math.floor(Math.random() * levels.length)];
      
      const newLog: LogEntry = {
        id: Date.now(),
        timestamp: new Date(),
        level: randomLevel,
        message: `System log message generated at ${new Date().toISOString()}`
      };
      
      setLogs(prev => [...prev.slice(-99), newLog]);
    }, 5000);
    
    return () => clearInterval(interval);
  }, []);

  useEffect(() => {
    if (autoScroll && logEndRef.current) {
      logEndRef.current.scrollIntoView({ behavior: 'smooth' });
    }
  }, [logs, autoScroll]);

  const filteredLogs = logs.filter(log => {
    if (levelFilter !== 'all' && log.level !== levelFilter) return false;
    if (searchQuery && !log.message.toLowerCase().includes(searchQuery.toLowerCase())) return false;
    return true;
  });

  return (
    <div className="panel-container" style={{ display: 'flex', flexDirection: 'column' }}>
      <div className="panel-card" style={{ flex: 1, display: 'flex', flexDirection: 'column', marginBottom: 0 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }}>
          <h2 className="panel-title" style={{ margin: 0, border: 'none' }}>System Logs</h2>
          <div style={{ display: 'flex', gap: '12px', alignItems: 'center' }}>
            <input 
              type="text" 
              placeholder="Search logs..." 
              className="btn"
              style={{ backgroundColor: 'rgba(15, 23, 42, 0.58)', cursor: 'text' }}
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
            <select 
              className="btn" 
              value={levelFilter} 
              onChange={(e) => setLevelFilter(e.target.value as any)}
            >
              <option value="all">All Levels</option>
              <option value="info">Info</option>
              <option value="warning">Warning</option>
              <option value="error">Error</option>
            </select>
            <label style={{ display: 'flex', alignItems: 'center', gap: '4px', fontSize: '0.9em', cursor: 'pointer' }}>
              <input 
                type="checkbox" 
                checked={autoScroll} 
                onChange={(e) => setAutoScroll(e.target.checked)} 
              />
              Auto-scroll
            </label>
            <button className="btn" onClick={() => setLogs([])}>Clear</button>
          </div>
        </div>
        
        <div className="log-container" style={{ flex: 1, height: 'auto' }}>
          {filteredLogs.length === 0 ? (
            <div style={{ color: 'var(--muted)', textAlign: 'center', marginTop: '100px' }}>
              No logs match the current filters.
            </div>
          ) : (
            filteredLogs.map(log => (
              <div key={log.id} className="log-entry" style={{ display: 'flex', gap: '8px' }}>
                <span className="log-time">[{log.timestamp.toLocaleTimeString()}]</span>
                <span className={`badge badge-${log.level === 'error' ? 'error' : log.level === 'warning' ? 'warning' : 'info'}`} style={{ width: '60px', textAlign: 'center' }}>
                  {log.level.toUpperCase()}
                </span>
                <span style={{ color: log.level === 'error' ? 'var(--red)' : log.level === 'warning' ? 'var(--yellow)' : 'var(--text)' }}>
                  {log.message}
                </span>
              </div>
            ))
          )}
          <div ref={logEndRef} />
        </div>
      </div>
    </div>
  );
};
