import React from 'react';
import type { ProjectInfo } from '../../hooks/useDashboard';

interface ProjectPanelProps {
  projectInfo: ProjectInfo | null;
  loading: boolean;
  onRefresh: () => void;
}

export const ProjectPanel: React.FC<ProjectPanelProps> = ({ projectInfo, loading, onRefresh }) => {
  return (
    <div className="panel-container">
      <div className="panel-card">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }}>
          <h2 className="panel-title" style={{ margin: 0, border: 'none' }}>Project Settings</h2>
          <button className="btn" onClick={onRefresh} disabled={loading}>
            {loading ? 'Refreshing...' : 'Refresh Info'}
          </button>
        </div>
        
        {projectInfo ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
            <div style={{ display: 'grid', gridTemplateColumns: '150px 1fr', gap: '8px', alignItems: 'center' }}>
              <div style={{ color: 'var(--muted)' }}>Project Name:</div>
              <div style={{ fontWeight: 'bold', fontSize: '1.2em' }}>{projectInfo.name}</div>
              
              <div style={{ color: 'var(--muted)' }}>Path:</div>
              <div style={{ fontFamily: 'monospace', backgroundColor: 'rgba(15, 23, 42, 0.58)', padding: '4px 8px', borderRadius: '4px' }}>
                {projectInfo.path}
              </div>
              
              <div style={{ color: 'var(--muted)' }}>Unity Version:</div>
              <div>{projectInfo.unityVersion}</div>
              
              <div style={{ color: 'var(--muted)' }}>Editor Status:</div>
              <div><span className="badge badge-success">Running</span></div>
            </div>
            
            <div style={{ marginTop: '16px', paddingTop: '16px', borderTop: '1px solid var(--line)' }}>
              <button className="btn btn-primary" onClick={() => console.log('Open in Explorer')}>
                Open in Explorer
              </button>
            </div>
          </div>
        ) : (
          <div style={{ color: 'var(--muted)', padding: '32px', textAlign: 'center' }}>
            {loading ? 'Loading project information...' : 'No project detected. Please ensure Unity is running.'}
          </div>
        )}
      </div>
      
      <div className="panel-card">
        <h3 className="panel-title">Recent Projects</h3>
        <ul style={{ listStyle: 'none', padding: 0, margin: 0 }}>
          <li style={{ padding: '8px 0', borderBottom: '1px solid var(--line)', display: 'flex', justifyContent: 'space-between' }}>
            <span>Neon Glitch</span>
            <span style={{ color: 'var(--muted)', fontSize: '0.9em' }}>E:\git\linalab\neon-glitch</span>
          </li>
          <li style={{ padding: '8px 0', borderBottom: '1px solid var(--line)', display: 'flex', justifyContent: 'space-between' }}>
            <span>Lux Demo</span>
            <span style={{ color: 'var(--muted)', fontSize: '0.9em' }}>E:\git\linalab\lux-demo</span>
          </li>
        </ul>
      </div>
    </div>
  );
};
