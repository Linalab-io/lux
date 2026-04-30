import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { findUnityProjectRoot } from '../src/project-root.js';

function withTempDirectory(run: (tempDirectory: string) => void): void {
  const tempDirectory = fs.mkdtempSync(path.join(os.tmpdir(), 'unity-ai-bridge-mcp-'));

  try {
    run(tempDirectory);
  } finally {
    fs.rmSync(tempDirectory, { recursive: true, force: true });
  }
}

function createProjectWithManifest(projectRoot: string): void {
  fs.mkdirSync(path.join(projectRoot, 'Packages'), { recursive: true });
  fs.writeFileSync(path.join(projectRoot, 'Packages', 'manifest.json'), '{}\n', 'utf8');
}

test('UNITY_PROJECT_PATH takes precedence over cwd upward search', () => {
  withTempDirectory((tempDirectory) => {
    const envProjectRoot = path.join(tempDirectory, 'env-project');
    const cwdProjectRoot = path.join(tempDirectory, 'cwd-project');
    const nestedCwd = path.join(cwdProjectRoot, 'Assets', 'Scripts');

    createProjectWithManifest(envProjectRoot);
    createProjectWithManifest(cwdProjectRoot);
    fs.mkdirSync(nestedCwd, { recursive: true });

    const result = findUnityProjectRoot({
      env: { UNITY_PROJECT_PATH: envProjectRoot },
      startDirectory: nestedCwd,
    });

    assert.equal(result, path.resolve(envProjectRoot));
  });
});

test('searches upward from cwd for Packages manifest marker', () => {
  withTempDirectory((tempDirectory) => {
    const projectRoot = path.join(tempDirectory, 'project');
    const nestedCwd = path.join(projectRoot, 'Assets', 'Editor', 'Tools');

    createProjectWithManifest(projectRoot);
    fs.mkdirSync(nestedCwd, { recursive: true });

    const result = findUnityProjectRoot({ env: {}, startDirectory: nestedCwd });

    assert.equal(result, path.resolve(projectRoot));
  });
});

test('accepts Library and Packages directory markers without a manifest file', () => {
  withTempDirectory((tempDirectory) => {
    const projectRoot = path.join(tempDirectory, 'project');
    const nestedCwd = path.join(projectRoot, 'Packages', 'com.example.package');

    fs.mkdirSync(path.join(projectRoot, 'Library'), { recursive: true });
    fs.mkdirSync(nestedCwd, { recursive: true });

    const result = findUnityProjectRoot({ env: {}, startDirectory: nestedCwd });

    assert.equal(result, path.resolve(projectRoot));
  });
});

test('returns undefined when no Unity project markers are found', () => {
  withTempDirectory((tempDirectory) => {
    const result = findUnityProjectRoot({ env: {}, startDirectory: tempDirectory });

    assert.equal(result, undefined);
  });
});

test('invalid UNITY_PROJECT_PATH fails before cwd fallback', () => {
  withTempDirectory((tempDirectory) => {
    const projectRoot = path.join(tempDirectory, 'project');
    const nestedCwd = path.join(projectRoot, 'Assets');

    createProjectWithManifest(projectRoot);
    fs.mkdirSync(nestedCwd, { recursive: true });

    assert.throws(
      () =>
        findUnityProjectRoot({
          env: { UNITY_PROJECT_PATH: path.join(tempDirectory, 'not-a-project') },
          startDirectory: nestedCwd,
        }),
      /UNITY_PROJECT_PATH does not point to a Unity project root/,
    );
  });
});
