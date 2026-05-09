type ApiMode = 'tauri' | 'rest';

const getApiMode = (): ApiMode => {
  return typeof window !== 'undefined' && '__TAURI__' in window ? 'tauri' : 'rest';
};

export async function invoke<T>(command: string, args?: Record<string, unknown>): Promise<T> {
  if (getApiMode() === 'tauri') {
    try {
      // @ts-ignore - Dynamic import to avoid breaking non-Tauri builds
      const { invoke: tauriInvoke } = await import(/* @vite-ignore */ '@tauri-apps/api/core');
      return (tauriInvoke as <U>(cmd: string, args?: any) => Promise<U>)<T>(command, args);
    } catch (e) {
      console.warn('Failed to load Tauri API, falling back to REST', e);
    }
  }
  
  const response = await fetch(`/api/${command}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(args || {}),
  });
  
  if (!response.ok) {
    throw new Error(`API error: ${response.statusText}`);
  }
  
  return response.json();
}

export const compileProject = () => invoke<void>('compile_project');
export const runTests = (mode: 'EditMode' | 'PlayMode') => invoke<void>('run_tests', { mode });
export const detectProject = () => invoke<{ name: string; path: string; unityVersion: string }>('detect_project');
export const listSkills = () => invoke<any[]>('list_skills');
export const getConfig = () => invoke<any>('get_config');
