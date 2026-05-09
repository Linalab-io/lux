import React from 'react';
import type { PanelType } from '../../hooks/useDashboard';

interface SidebarProps {
  activePanel: PanelType;
  onSelectPanel: (panel: PanelType) => void;
  collapsed: boolean;
  onToggleCollapse: () => void;
}

export const Sidebar: React.FC<SidebarProps> = ({
  activePanel,
  onSelectPanel,
  collapsed,
  onToggleCollapse
}) => {
  const navItems: { id: PanelType; label: string; icon: string }[] = [
    { id: 'overview', label: 'Dashboard', icon: '📊' },
    { id: 'compile', label: 'Compile', icon: '🔨' },
    { id: 'tests', label: 'Tests', icon: '🧪' },
    { id: 'logs', label: 'Logs', icon: '📝' },
    { id: 'project', label: 'Project', icon: '📁' },
    { id: 'skills', label: 'Skills', icon: '🧩' },
    { id: 'visual-report', label: 'Visual Report', icon: '👁️' },
  ];

  return (
    <div className={`sidebar ${collapsed ? 'collapsed' : 'expanded'}`}>
      <div className="sidebar-header">
        {collapsed ? 'L' : 'LUX OS'}
      </div>
      
      <div className="sidebar-nav">
        {navItems.map(item => (
          <div
            key={item.id}
            className={`nav-item ${activePanel === item.id ? 'active' : ''}`}
            onClick={() => onSelectPanel(item.id)}
            title={collapsed ? item.label : undefined}
          >
            <span className="nav-icon">{item.icon}</span>
            <span className="nav-label">{item.label}</span>
          </div>
        ))}
      </div>
      
      <div className="sidebar-footer">
        <button className="collapse-toggle" onClick={onToggleCollapse} title={collapsed ? "Expand" : "Collapse"}>
          {collapsed ? '▶' : '◀'}
        </button>
        <div className="version-label">v0.1.0</div>
      </div>
    </div>
  );
};
