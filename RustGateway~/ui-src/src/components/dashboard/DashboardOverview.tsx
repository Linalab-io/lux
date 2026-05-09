import React from 'react';
import type { ProjectInfo, PanelType } from '../../hooks/useDashboard';

interface DashboardOverviewProps {
  projectInfo: ProjectInfo | null;
  onNavigate: (panel: PanelType) => void;
}

export const DashboardOverview: React.FC<DashboardOverviewProps> = ({ projectInfo, onNavigate }) => {
  return (
    <div className="panel-container">
      <div className="panel-card">
        <h2 className="panel-title">Welcome to Lux OS</h2>
        <p>System is online and ready.</p>
        
        {projectInfo && (
          <div style={{ marginTop: '16px', padding: '12px', backgroundColor: 'rgba(56, 189, 248, 0.1)', borderLeft: '4px solid var(--blue)' }}>
            <strong>Active Project:</strong> {projectInfo.name} ({projectInfo.unityVersion})
          </div>
        )}
      </div>
      
      <div className="grid-3">
        <div className="stat-card" onClick={() => onNavigate('compile')} style={{ cursor: 'pointer' }}>
          <div className="stat-label">Build Status</div>
          <div className="stat-value" style={{ color: 'var(--green)' }}>Ready</div>
          <div className="badge badge-success">0 Errors</div>
        </div>
        
        <div className="stat-card" onClick={() => onNavigate('tests')} style={{ cursor: 'pointer' }}>
          <div className="stat-label">Test Coverage</div>
          <div className="stat-value">85%</div>
          <div className="badge badge-info">120 Passed</div>
        </div>
        
        <div className="stat-card" onClick={() => onNavigate('skills')} style={{ cursor: 'pointer' }}>
          <div className="stat-label">Active Skills</div>
          <div className="stat-value">4</div>
          <div className="badge badge-warning">2 Updates</div>
        </div>
      </div>
      
      <div className="grid-2" style={{ marginTop: '16px' }}>
        <div className="panel-card">
          <h3 className="panel-title">Quick Actions</h3>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
            <button className="btn btn-primary" onClick={() => onNavigate('compile')}>Run Full Compile</button>
            <button className="btn" onClick={() => onNavigate('tests')}>Run All Tests</button>
            <button className="btn" onClick={() => onNavigate('visual-report')}>Capture Visual Baseline</button>
          </div>
        </div>
        
        <div className="panel-card">
          <h3 className="panel-title">Recent Activity</h3>
          <div style={{ fontSize: '0.9em', color: 'var(--muted)' }}>
            <div style={{ padding: '8px 0', borderBottom: '1px solid var(--line)' }}>
              <span style={{ color: 'var(--muted)', marginRight: '8px' }}>10:42 AM</span>
              Project loaded successfully
            </div>
            <div style={{ padding: '8px 0', borderBottom: '1px solid var(--line)' }}>
              <span style={{ color: 'var(--muted)', marginRight: '8px' }}>10:45 AM</span>
              Tests completed (42 passed)
            </div>
            <div style={{ padding: '8px 0' }}>
              <span style={{ color: 'var(--muted)', marginRight: '8px' }}>11:02 AM</span>
              Skill 'UI Builder' updated
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
