import fs from 'node:fs';
import path from 'node:path';

const UNITY_PROJECT_PATH_ENV = 'UNITY_PROJECT_PATH';

export interface FileSystemProbe {
  existsSync(filePath: string): boolean;
  statSync(filePath: string): fs.Stats;
}

export interface ProjectRootSearchOptions {
  env?: NodeJS.ProcessEnv;
  startDirectory?: string;
  fs?: FileSystemProbe;
}

export function findUnityProjectRoot(options: ProjectRootSearchOptions = {}): string | undefined {
  const env = options.env ?? process.env;
  const fileSystem = options.fs ?? fs;
  const configuredProjectPath = env[UNITY_PROJECT_PATH_ENV]?.trim();

  if (configuredProjectPath) {
    const projectRoot = path.resolve(configuredProjectPath);

    if (isUnityProjectRoot(projectRoot, fileSystem)) {
      return projectRoot;
    }

    throw new Error(`${UNITY_PROJECT_PATH_ENV} does not point to a Unity project root: ${projectRoot}`);
  }

  let currentDirectory = path.resolve(options.startDirectory ?? process.cwd());

  while (true) {
    if (isUnityProjectRoot(currentDirectory, fileSystem)) {
      return currentDirectory;
    }

    const parentDirectory = path.dirname(currentDirectory);
    if (parentDirectory === currentDirectory) {
      return undefined;
    }

    currentDirectory = parentDirectory;
  }
}

export function isUnityProjectRoot(candidatePath: string, fileSystem: FileSystemProbe = fs): boolean {
  const packagesPath = path.join(candidatePath, 'Packages');
  const libraryPath = path.join(candidatePath, 'Library');
  const manifestPath = path.join(packagesPath, 'manifest.json');

  if (isFile(manifestPath, fileSystem)) {
    return true;
  }

  return isDirectory(packagesPath, fileSystem) && isDirectory(libraryPath, fileSystem);
}

function isDirectory(targetPath: string, fileSystem: FileSystemProbe): boolean {
  try {
    return fileSystem.existsSync(targetPath) && fileSystem.statSync(targetPath).isDirectory();
  } catch {
    return false;
  }
}

function isFile(targetPath: string, fileSystem: FileSystemProbe): boolean {
  try {
    return fileSystem.existsSync(targetPath) && fileSystem.statSync(targetPath).isFile();
  } catch {
    return false;
  }
}
