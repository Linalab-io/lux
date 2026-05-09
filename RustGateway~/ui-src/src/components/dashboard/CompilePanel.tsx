import React, { useState, useEffect } from 'react';
import { compileProject } from '../../lib/api';

export const CompilePanel: React.FC = () => {
  const [status, setStatus] = useState<'idle' | 'compiling' | 'success' | 'error'>('idle');
  const [logs, setLogs] = useState<string[]>([]);
  const [duration, setDuration] = useState(0);
  const [errorCount, setErrorCount] = useState(0);

  useEffect(() => {
    let interval: number;
    if (status === 'compiling') {
      interval = window.setInterval(() => {
        setDuration(prev => prev + 1);
      }, 1000);
    }
    return () => clearInterval(interval);
  }, [status]);

  const handleCompile = async () => {
    setStatus('compiling');
    setLogs(['Starting compilation...']);
    setDuration(0);
    setErrorCount(0);
    
    try {
      setTimeout(() => setLogs(prev => [...prev, 'Resolving dependencies...']), 1000);
      setTimeout(() => setLogs(prev => [...prev, 'Compiling scripts...']), 2000);
      
      await compileProject();
      
      setLogs(prev => [...prev, 'Compilation successful!']);
      setStatus('success');
    } catch (err) {
      setLogs(prev => [...prev, `Error: ${err instanceof Error ? err.message : 'Unknown error'}`]);
      setStatus('error');
      setErrorCount(1);
    }
  };

  return (
    <div className="panel-container">
      <div className="panel-card">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }}>
          <h2 className="panel-title" style={{ margin: 0, border: 'none' }}>Compilation</h2>
          <div style={{ display: 'flex', gap: '12px', alignItems: 'center' }}>
            {status !== 'idle' && (
              <span style={{ color: 'var(--muted)' }}>Time: {duration}s</span>
            )}
            {status === 'error' && (
              <span className="badge badge-error">{errorCount} Errors</span>
            )}
            {status === 'success' && (
              <span className="badge badge-success">Success</span>
            )}
            {status === 'compiling' && (
              <span className="badge badge-warning">Compiling...</span>
            )}
            <button 
              className="btn btn-primary" 
              onClick={handleCompile}
              disabled={status === 'compiling'}
            >
              {status === 'compiling' ? 'Compiling...' : 'Compile Project'}
            </button>
          </div>
        </div>
        
        <div className="log-container">
          {logs.length === 0 ? (
            <div style={{ color: 'var(--muted)', textAlign: 'center', marginTop: '100px' }}>
              No compilation logs yet. Click Compile to start.
            </div>
          ) : (
            logs.map((log, i) => (
              <div key={i} className="log-entry" style={{ color: log.startsWith('Error') ? 'var(--red)' : 'var(--text)' }}>
                <span className="log-time">[{new Date().toLocaleTimeString()}]</span>
                {log}
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
};
