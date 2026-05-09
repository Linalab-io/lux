import React, { useState, useEffect } from 'react';
import { listSkills } from '../../lib/api';

interface Skill {
  id: string;
  name: string;
  version: string;
  description: string;
  installed: boolean;
}

export const SkillMarketplace: React.FC = () => {
  const [skills, setSkills] = useState<Skill[]>([]);
  const [loading, setLoading] = useState(true);
  const [tab, setTab] = useState<'installed' | 'available'>('installed');
  const [searchQuery, setSearchQuery] = useState('');

  useEffect(() => {
    const fetchSkills = async () => {
      setLoading(true);
      try {
        await listSkills();
      } catch (e) {
        setSkills([
          { id: '1', name: 'UI Builder', version: '1.2.0', description: 'Visual UI editor for Unity UI Toolkit', installed: true },
          { id: '2', name: 'Node Graph', version: '0.9.5', description: 'Visual scripting node editor', installed: true },
          { id: '3', name: 'Asset Browser', version: '2.0.1', description: 'Advanced asset management and search', installed: false },
          { id: '4', name: 'AI Assistant', version: '1.0.0', description: 'Copilot integration for Unity', installed: true },
          { id: '5', name: 'Level Generator', version: '0.5.0', description: 'Procedural generation tools', installed: false },
        ]);
      } finally {
        setLoading(false);
      }
    };
    
    fetchSkills();
  }, []);

  const filteredSkills = skills.filter(skill => {
    if (tab === 'installed' && !skill.installed) return false;
    if (tab === 'available' && skill.installed) return false;
    if (searchQuery && !skill.name.toLowerCase().includes(searchQuery.toLowerCase()) && !skill.description.toLowerCase().includes(searchQuery.toLowerCase())) return false;
    return true;
  });

  const toggleInstall = (id: string) => {
    setSkills(skills.map(s => s.id === id ? { ...s, installed: !s.installed } : s));
  };

  return (
    <div className="panel-container">
      <div className="panel-card">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }}>
          <h2 className="panel-title" style={{ margin: 0, border: 'none' }}>Skill Marketplace</h2>
          <input 
            type="text" 
            placeholder="Search skills..." 
            className="btn"
            style={{ backgroundColor: 'rgba(15, 23, 42, 0.58)', cursor: 'text', width: '250px' }}
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
          />
        </div>
        
        <div style={{ display: 'flex', gap: '16px', borderBottom: '1px solid var(--line)', marginBottom: '16px' }}>
          <div 
            style={{ padding: '8px 16px', cursor: 'pointer', borderBottom: tab === 'installed' ? '2px solid var(--blue)' : '2px solid transparent', color: tab === 'installed' ? 'var(--blue)' : 'var(--muted)' }}
            onClick={() => setTab('installed')}
          >
            Installed ({skills.filter(s => s.installed).length})
          </div>
          <div 
            style={{ padding: '8px 16px', cursor: 'pointer', borderBottom: tab === 'available' ? '2px solid var(--blue)' : '2px solid transparent', color: tab === 'available' ? 'var(--blue)' : 'var(--muted)' }}
            onClick={() => setTab('available')}
          >
            Available ({skills.filter(s => !s.installed).length})
          </div>
        </div>
        
        {loading ? (
          <div style={{ textAlign: 'center', padding: '32px', color: 'var(--muted)' }}>Loading skills...</div>
        ) : filteredSkills.length === 0 ? (
          <div style={{ textAlign: 'center', padding: '32px', color: 'var(--muted)' }}>No skills found.</div>
        ) : (
          <div className="grid-2">
            {filteredSkills.map(skill => (
              <div key={skill.id} style={{ border: '1px solid var(--line)', borderRadius: '6px', padding: '16px', backgroundColor: 'rgba(15, 23, 42, 0.58)', display: 'flex', flexDirection: 'column' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '8px' }}>
                  <h3 style={{ margin: 0, color: 'var(--text)' }}>{skill.name}</h3>
                  <span className="badge badge-info">v{skill.version}</span>
                </div>
                <p style={{ color: 'var(--muted)', fontSize: '0.9em', flex: 1, margin: '8px 0 16px 0' }}>{skill.description}</p>
                <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                  <button 
                    className={`btn ${skill.installed ? '' : 'btn-primary'}`}
                    onClick={() => toggleInstall(skill.id)}
                  >
                    {skill.installed ? 'Uninstall' : 'Install'}
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};
