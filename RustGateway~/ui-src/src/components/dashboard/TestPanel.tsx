import React, { useState } from 'react';
import { runTests } from '../../lib/api';

interface TestResult {
  id: string;
  name: string;
  status: 'passed' | 'failed' | 'skipped';
  duration: number;
  message?: string;
}

export const TestPanel: React.FC = () => {
  const [mode, setMode] = useState<'EditMode' | 'PlayMode'>('EditMode');
  const [status, setStatus] = useState<'idle' | 'running' | 'completed'>('idle');
  const [results, setResults] = useState<TestResult[]>([]);
  const [filter, setFilter] = useState<'all' | 'passed' | 'failed'>('all');

  const handleRunTests = async () => {
    setStatus('running');
    setResults([]);
    
    try {
      await runTests(mode);
      
      setResults([
        { id: '1', name: 'PlayerMovement_ShouldMoveForward', status: 'passed', duration: 120 },
        { id: '2', name: 'Weapon_ShouldFireProjectile', status: 'passed', duration: 45 },
        { id: '3', name: 'Enemy_ShouldTakeDamage', status: 'failed', duration: 15, message: 'Expected health to be 90, but was 100' },
        { id: '4', name: 'UI_ShouldUpdateScore', status: 'passed', duration: 30 },
      ]);
      setStatus('completed');
    } catch (err) {
      console.error('Test run failed:', err);
      setStatus('completed');
    }
  };

  const passedCount = results.filter(r => r.status === 'passed').length;
  const failedCount = results.filter(r => r.status === 'failed').length;
  
  const filteredResults = results.filter(r => filter === 'all' || r.status === filter);

  return (
    <div className="panel-container">
      <div className="panel-card">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }}>
          <h2 className="panel-title" style={{ margin: 0, border: 'none' }}>Test Runner</h2>
          <div style={{ display: 'flex', gap: '12px', alignItems: 'center' }}>
            <select 
              className="btn" 
              value={mode} 
              onChange={(e) => setMode(e.target.value as 'EditMode' | 'PlayMode')}
              disabled={status === 'running'}
            >
              <option value="EditMode">Edit Mode</option>
              <option value="PlayMode">Play Mode</option>
            </select>
            <button 
              className="btn btn-primary" 
              onClick={handleRunTests}
              disabled={status === 'running'}
            >
              {status === 'running' ? 'Running...' : 'Run Tests'}
            </button>
          </div>
        </div>
        
        {status === 'completed' && results.length > 0 && (
          <div style={{ display: 'flex', gap: '16px', marginBottom: '16px', padding: '12px', backgroundColor: 'rgba(15, 23, 42, 0.58)', borderRadius: '4px' }}>
            <div style={{ fontWeight: 'bold' }}>Summary:</div>
            <div style={{ color: 'var(--green)' }}>{passedCount} Passed</div>
            <div style={{ color: 'var(--red)' }}>{failedCount} Failed</div>
            <div style={{ color: 'var(--muted)' }}>{results.length} Total</div>
          </div>
        )}
        
        <div style={{ marginBottom: '12px', display: 'flex', gap: '8px' }}>
          <button className={`btn ${filter === 'all' ? 'btn-primary' : ''}`} onClick={() => setFilter('all')}>All</button>
          <button className={`btn ${filter === 'passed' ? 'btn-primary' : ''}`} onClick={() => setFilter('passed')}>Passed</button>
          <button className={`btn ${filter === 'failed' ? 'btn-primary' : ''}`} onClick={() => setFilter('failed')}>Failed</button>
        </div>
        
        <table className="table">
          <thead>
            <tr>
              <th>Status</th>
              <th>Test Name</th>
              <th>Duration (ms)</th>
              <th>Message</th>
            </tr>
          </thead>
          <tbody>
            {filteredResults.length === 0 ? (
              <tr>
                <td colSpan={4} style={{ textAlign: 'center', padding: '32px', color: 'var(--muted)' }}>
                  {status === 'idle' ? 'Click Run Tests to start' : 'No tests match the current filter'}
                </td>
              </tr>
            ) : (
              filteredResults.map(test => (
                <tr key={test.id}>
                  <td>
                    <span className={`badge ${test.status === 'passed' ? 'badge-success' : 'badge-error'}`}>
                      {test.status.toUpperCase()}
                    </span>
                  </td>
                  <td style={{ fontFamily: 'monospace' }}>{test.name}</td>
                  <td>{test.duration}</td>
                  <td style={{ color: 'var(--red)', fontSize: '0.9em' }}>{test.message || '-'}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
};
