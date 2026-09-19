import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { WORKSPACE_ROOT } from './product.mjs';

const platforms = ['macos', 'windows'];
const producers = new Set([
  'managed',
  'history-contract',
  'sqlite-macos',
  'sqlite-windows',
  'native',
  'bootstrap',
  'runtime',
  'retired'
]);
const ownerships = new Set(['private', 'dependency', 'bootstrap']);
const rootFiles = new Set([
  'winhttp.dll',
  'doorstop_config.ini',
  'libdoorstop.dylib'
]);
const bootstrapPaths = new Set([
  'BepInEx/core',
  'BepInEx/cache',
  'BepInEx/config/BepInEx.cfg',
  'BepInEx/LogOutput.log',
  'BepInEx/plugins/.gitkeep',
  ...rootFiles
]);

function allowedOwnership(entry) {
  if (entry.ownership === 'bootstrap') return bootstrapPaths.has(entry.path);
  if (/^BepInEx\/plugins\/[^/]+$/.test(entry.path)) return true;
  if (entry.ownership !== 'private') return false;
  return (
    entry.path === 'BepInEx/config/BazaarPlusPlus.cfg' ||
    /^TheBazaar\.app\/Contents\/Plugins\/GfxPluginBpp[^/]+\.bundle$/.test(
      entry.path
    ) ||
    /^TheBazaar_Data\/Plugins\/x86_64\/GfxPluginBpp[^/]+\.dll$/.test(entry.path)
  );
}

export function assertRelativePayloadPath(value) {
  if (
    typeof value !== 'string' ||
    !value ||
    value.includes('\\') ||
    value.includes(':') ||
    value.includes('\0') ||
    value.split('/').some((part) => !part || part === '.' || part === '..')
  ) {
    throw new Error(`Unsafe payload path: ${JSON.stringify(value)}`);
  }
  return value;
}

export function validateInventory(inventory) {
  if (
    inventory?.schemaVersion !== 1 ||
    !Array.isArray(inventory.files) ||
    !inventory.files.length
  )
    throw new Error('Invalid Payload Inventory');
  const seen = [];
  for (const entry of inventory.files) {
    const relativePath = assertRelativePayloadPath(entry.path);
    if (
      (!relativePath.includes('/') && !rootFiles.has(relativePath)) ||
      !ownerships.has(entry.ownership) ||
      !producers.has(entry.producer) ||
      ![undefined, 'file', 'directory'].includes(entry.kind)
    )
      throw new Error(`Invalid payload entry: ${relativePath}`);
    if (entry.kind === 'directory' && relativePath.split('/').length < 2)
      throw new Error(`Overbroad payload directory: ${relativePath}`);
    if (
      entry.platforms !== undefined &&
      (!Array.isArray(entry.platforms) ||
        !entry.platforms.length ||
        entry.platforms.some((platform) => !platforms.includes(platform)))
    )
      throw new Error(`Invalid payload platforms: ${relativePath}`);
    for (const child of entry.requiredEntries ?? [])
      assertRelativePayloadPath(child);
    for (const previous of seen) {
      const a = previous.path.toLowerCase();
      const b = relativePath.toLowerCase();
      if (a === b || b.startsWith(`${a}/`) || a.startsWith(`${b}/`))
        throw new Error(
          `Overlapping payload paths: ${previous.path}, ${relativePath}`
        );
    }
    if (!allowedOwnership(entry))
      throw new Error(
        `Payload ownership is outside the approved install roots: ${relativePath}`
      );
    seen.push(entry);
  }
  return inventory;
}

export function readInventory(workspaceRoot = WORKSPACE_ROOT) {
  return validateInventory(
    JSON.parse(
      fs.readFileSync(path.join(workspaceRoot, 'release/payload.json'), 'utf8')
    )
  );
}

export function platformInventory(platform, inventory = readInventory()) {
  if (!platforms.includes(platform))
    throw new Error(`Unsupported payload platform: ${platform}`);
  return inventory.files.filter(
    (entry) => !entry.platforms || entry.platforms.includes(platform)
  );
}

export function payloadEntry(relativePath, entries) {
  assertRelativePayloadPath(relativePath);
  return entries.find(
    (entry) =>
      entry.path === relativePath ||
      (entry.kind === 'directory' && relativePath.startsWith(`${entry.path}/`))
  );
}

export function assertShippedPayloadPaths(
  relativePaths,
  platform,
  inventory = readInventory()
) {
  const entries = platformInventory(platform, inventory);
  for (const relativePath of relativePaths) {
    const entry = payloadEntry(relativePath, entries);
    if (!entry || ['runtime', 'retired'].includes(entry.producer))
      throw new Error(
        `Undeclared or forbidden ${platform} release payload path: ${relativePath}`
      );
  }
}

export function requiredPayloadPaths(platform, inventory = readInventory()) {
  return platformInventory(platform, inventory)
    .filter(
      (entry) =>
        !entry.optional && !['runtime', 'retired'].includes(entry.producer)
    )
    .flatMap((entry) =>
      entry.kind === 'directory'
        ? (entry.requiredEntries ?? []).map((child) => `${entry.path}/${child}`)
        : [entry.path]
    );
}

function xml(value) {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('"', '&quot;')
    .replaceAll('<', '&lt;');
}

export function renderManagedPayloadTargets(workspaceRoot = WORKSPACE_ROOT) {
  const inventory = readInventory(workspaceRoot);
  const digest = crypto
    .createHash('sha256')
    .update(fs.readFileSync(path.join(workspaceRoot, 'release/payload.json')))
    .digest('hex')
    .toUpperCase();
  const entries = inventory.files.filter((entry) =>
    ['managed', 'history-contract', 'sqlite-macos', 'sqlite-windows'].includes(
      entry.producer
    )
  );
  const items = entries
    .map((entry) => {
      const source =
        entry.producer === 'managed'
          ? `$(TargetDir)${path.posix.basename(entry.path)}`
          : entry.producer === 'history-contract'
            ? '$(HistoryDatabaseContractFile)'
            : entry.producer === 'sqlite-macos'
              ? '$(FixedMacSqliteNativeFile)'
              : '$(FixedWindowsSqliteNativeFile)';
      const conditions = [];
      if (entry.optional) conditions.push(`Exists('${source}')`);
      if (entry.platforms)
        conditions.push(`'$(BppPayloadPlatform)' == '${entry.platforms[0]}'`);
      return `      <BppPayloadFile Include="${xml(source)}"${conditions.length ? ` Condition="${xml(conditions.join(' and '))}"` : ''}><PayloadPath>${entry.path}</PayloadPath><ProductAssembly>${entry.assembly === true}</ProductAssembly></BppPayloadFile>`;
    })
    .join('\n');
  return `<Project>\n  <!-- Generated from release/payload.json by node release.mjs sync. -->\n  <Target Name="ValidatePayloadInventoryProjection" BeforeTargets="PrepareForBuild;CollectPayloadFiles">\n    <GetFileHash Files="$(MSBuildThisFileDirectory)../payload.json" Algorithm="SHA256"><Output TaskParameter="Items" ItemName="_PayloadInventoryHash" /></GetFileHash>\n    <Error Condition="'%(_PayloadInventoryHash.FileHash)' != '${digest}'" Text="Payload Inventory projection is stale. Run node release.mjs sync." />\n  </Target>\n  <Target Name="CollectPayloadFiles" DependsOnTargets="ValidatePayloadInventoryProjection">\n    <PropertyGroup>\n      <BppPayloadPlatform Condition="'$(BppReleasePlatform)' != ''">$(BppReleasePlatform)</BppPayloadPlatform>\n      <BppPayloadPlatform Condition="'$(BppPayloadPlatform)' == '' and '$(IsMacHost)' == 'true'">macos</BppPayloadPlatform>\n      <BppPayloadPlatform Condition="'$(BppPayloadPlatform)' == '' and '$(IsWindowsHost)' == 'true'">windows</BppPayloadPlatform>\n    </PropertyGroup>\n    <ItemGroup>\n${items}\n    </ItemGroup>\n  </Target>\n  <Target Name="ValidatePayloadAssemblyVersions" DependsOnTargets="CollectPayloadFiles">\n    <ItemGroup><_ProductAssembly Include="@(BppPayloadFile)" Condition="'%(BppPayloadFile.ProductAssembly)' == 'true'" /></ItemGroup>\n    <GetAssemblyIdentity AssemblyFiles="@(_ProductAssembly)"><Output TaskParameter="Assemblies" ItemName="_ProductAssemblyIdentity" /></GetAssemblyIdentity>\n    <Error Condition="'%(_ProductAssemblyIdentity.Version)' != '$(BppVersion).0'" Text="Payload assembly %(_ProductAssemblyIdentity.Identity) has version %(_ProductAssemblyIdentity.Version); expected $(BppVersion).0." />\n  </Target>\n</Project>\n`;
}

export function synchronizePayloadProjection(
  workspaceRoot = WORKSPACE_ROOT,
  { check = false } = {}
) {
  const outputPath = path.join(
    workspaceRoot,
    'release/generated/Payload.targets'
  );
  const expected = renderManagedPayloadTargets(workspaceRoot);
  if (check) {
    if (
      !fs.existsSync(outputPath) ||
      fs.readFileSync(outputPath, 'utf8') !== expected
    )
      throw new Error(
        'Payload Inventory projection is stale. Run node release.mjs sync.'
      );
  } else {
    fs.mkdirSync(path.dirname(outputPath), { recursive: true });
    fs.writeFileSync(outputPath, expected);
  }
}
