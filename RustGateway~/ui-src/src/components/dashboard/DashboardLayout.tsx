import React from 'react';
import { Sidebar } from '../sidebar/Sidebar';
import { useDashboard } from '../../hooks/useDashboard';
import { DashboardOverview } from './DashboardOverview';
import { CompilePanel } from './CompilePanel';
import { TestPanel } from './TestPanel';
import { LogPanel } from './LogPanel';
import { ProjectPanel } from './ProjectPanel';
import { SkillMarketplace } from './SkillMarketplace';
import { VisualReportPanel } from './VisualReportPanel';

export const DashboardLayout: React.FC = () => {
  const {
    activePanel,
    setActivePanel,
    sidebarCollapsed,
    toggleSidebar,
    projectInfo,
    serverStatus,
    loading,
    refreshProjectInfo
  } = useDashboard();

  const renderPanel = () => {
    switch (activePanel) {
      case 'overview':
        return <DashboardOverview projectInfo={projectInfo} onNavigate={setActivePanel} />;
      case 'compile':
        return <CompilePanel />;
      case 'tests':
        return <TestPanel />;
      case 'logs':
        return <LogPanel />;
      case 'project':
        return <ProjectPanel projectInfo={projectInfo} loading={loading} onRefresh={refreshProjectInfo} />;
      case 'skills':
        return <SkillMarketplace />;
      case 'visual-report':
        return <VisualReportPanel />;
      default:
        return <DashboardOverview projectInfo={projectInfo} onNavigate={setActivePanel} />;
    }
  };

  return (
    <div className="dashboard-layout">
      <Sidebar 
        activePanel={activePanel} 
        onSelectPanel={setActivePanel} 
        collapsed={sidebarCollapsed} 
        onToggleCollapse={toggleSidebar} 
      />
      
      <div className="dashboard-content">
        <div className="dashboard-header">
          <div className="project-info">
            {projectInfo ? (
              <>
                <span>{projectInfo.name}</span>
                <span className="badge badge-info" style={{ fontSize: '0.7em' }}>{projectInfo.unityVersion}</span>
              </>
            ) : (
              <span style={{ color: 'var(--muted)' }}>No project loaded</span>
            )}
          </div>
          
          <div className="connection-status">
            <div className={`status-dot ${serverStatus}`} />
            <span>{serverStatus === 'connected' ? 'Connected' : 'Disconnected'}</span>
          </div>
        </div>
        
        {renderPanel()}
      </div>
    </div>
  );
};
