#!/usr/bin/env python3
"""Generate the standalone launcher project using only Python's standard library."""
import hashlib
from pathlib import Path
import plistlib
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PRODUCT = 'BazaarPlusPlus'
# The keychain service and the simulator access group derive from this id; changing it
# starts a new App sandbox and keychain, so saved logins and downloaded games stay behind.
BUNDLE_ID = 'com.bazaarplusplus.ios'


def generate():
    objects = {}

    def add(identity, isa, **fields):
        key = hashlib.sha1(('bpp-ios:' + identity).encode()).hexdigest()[:24].upper()
        objects[key] = dict(isa=isa, **fields)
        return key

    def file(path, kind, tree='SOURCE_ROOT'):
        return add('file:' + path, 'PBXFileReference', path=path,
                   lastKnownFileType=kind, sourceTree=tree)

    source_files = [file('Host/main.mm', 'sourcecode.cpp.objcpp')]
    source_files += [file(p.relative_to(ROOT / 'app').as_posix(), 'sourcecode.swift') for p in sorted((ROOT / 'app').glob('*/*.swift'))]
    sources = add('sources', 'PBXSourcesBuildPhase', buildActionMask=2147483647,
                  files=[add('compile:' + f, 'PBXBuildFile', fileRef=f) for f in source_files],
                  runOnlyForDeploymentPostprocessing=0)
    framework = file('UnityFramework.framework', 'wrapper.framework', 'BPP_FRAMEWORK_PRODUCTS')
    embed = add('embed', 'PBXCopyFilesBuildPhase', buildActionMask=2147483647,
                dstPath='', dstSubfolderSpec=10, name='Embed Runtime',
                files=[add('embed-framework', 'PBXBuildFile', fileRef=framework,
                           settings={'ATTRIBUTES': ['CodeSignOnCopy', 'RemoveHeadersOnCopy']})],
                runOnlyForDeploymentPostprocessing=0)
    # main.mm loads the framework with NSBundle, so it is embedded, not linked.
    frameworks = add('frameworks', 'PBXFrameworksBuildPhase', buildActionMask=2147483647,
                     files=[], runOnlyForDeploymentPostprocessing=0)
    # Icon Composer document; actool also derives the pre-iOS 26 PNG icons from it.
    assets = file('../../bazaarplusplus-installer/src-tauri/icons/source/AppIcon.icon', 'folder.iconcomposer.icon')
    resources = add('resources', 'PBXResourcesBuildPhase', buildActionMask=2147483647,
                    files=[add('resource:' + assets, 'PBXBuildFile', fileRef=assets)],
                    runOnlyForDeploymentPostprocessing=0)
    package = add('package', 'PBXShellScriptBuildPhase', buildActionMask=2147483647,
                  name='Package Runtime Resources', files=[], alwaysOutOfDate=1,
                  inputPaths=['$(TARGET_BUILD_DIR)/$(INFOPLIST_PATH)',
                              '$(TARGET_BUILD_DIR)/$(EXECUTABLE_PATH)',
                              '$(TARGET_BUILD_DIR)/$(FRAMEWORKS_FOLDER_PATH)/UnityFramework.framework/UnityFramework'],
                  outputPaths=[], runOnlyForDeploymentPostprocessing=0, shellPath='/bin/sh',
                  shellScript='set -eu\n'
                  '/usr/bin/rsync -a --delete "$BPP_RUNTIME_ROOT/export/Data/" "$TARGET_BUILD_DIR/$WRAPPER_NAME/Data/"\n'
                  '/usr/bin/python3 "$BPP_REPOSITORY_ROOT/tools/package-app.py" '
                  '"$TARGET_BUILD_DIR/$WRAPPER_NAME" "$BPP_RUNTIME_PLATFORM" --xcode\n')
    settings = {
        'PRODUCT_NAME': PRODUCT, 'PRODUCT_BUNDLE_IDENTIFIER': BUNDLE_ID,
        'INFOPLIST_FILE': 'Resources/Info.plist', 'CODE_SIGN_STYLE': 'Automatic',
        'ASSETCATALOG_COMPILER_APPICON_NAME': 'AppIcon',
        'SDKROOT': 'iphoneos', 'SUPPORTED_PLATFORMS': 'iphoneos iphonesimulator',
        'TARGETED_DEVICE_FAMILY': '1,2', 'IPHONEOS_DEPLOYMENT_TARGET': '15.0',
        'CLANG_ENABLE_OBJC_ARC': 'YES', 'CLANG_ENABLE_MODULES': 'YES',
        'CLANG_CXX_LANGUAGE_STANDARD': 'c++17', 'SWIFT_VERSION': '5.0',
        'ENABLE_USER_SCRIPT_SANDBOXING': 'NO', 'ENABLE_BITCODE': 'NO',
        'BPP_REPOSITORY_ROOT': '$(SRCROOT)/..',
        'BPP_RUNTIME_PLATFORM[sdk=iphoneos*]': 'device',
        'BPP_RUNTIME_PLATFORM[sdk=iphonesimulator*]': 'simulator',
        'BPP_RUNTIME_ROOT': '$(BPP_REPOSITORY_ROOT)/build/steam-app/$(BPP_RUNTIME_PLATFORM)',
        'BPP_FRAMEWORK_PRODUCTS': '$(BPP_RUNTIME_ROOT)/derived/Build/Products/Release$(EFFECTIVE_PLATFORM_NAME)',
        'FRAMEWORK_SEARCH_PATHS': ['$(inherited)', '$(BPP_FRAMEWORK_PRODUCTS)'],
        'LD_RUNPATH_SEARCH_PATHS': ['$(inherited)', '@executable_path/Frameworks'],
        'OTHER_LDFLAGS': ['$(inherited)', '-framework', 'UIKit', '-framework', 'Foundation', '-framework', 'CoreGraphics'],
        # Simulator builds stay ad hoc: a team would make Xcode inject its own entitlements
        # ahead of simulator.entitlements and move the keychain access group.
        'DEVELOPMENT_TEAM[sdk=iphonesimulator*]': '',
        'CODE_SIGN_IDENTITY[sdk=iphonesimulator*]': '-',
        'CODE_SIGN_STYLE[sdk=iphonesimulator*]': 'Manual',
        # Xcode embeds simulator entitlements itself (XML and DER sections); a manual
        # sectcreate would be shadowed by its generated copy.
        'CODE_SIGN_ENTITLEMENTS[sdk=iphonesimulator*]': 'Resources/simulator.entitlements',
        'CODE_SIGN_INJECT_BASE_ENTITLEMENTS[sdk=iphonesimulator*]': 'NO',
        'BPP_STEAM_TEST_CONTROL': '0',
        'GCC_PREPROCESSOR_DEFINITIONS': ['$(inherited)', 'BPP_STEAM_TEST_CONTROL=0'],
        'GCC_PREPROCESSOR_DEFINITIONS[sdk=iphonesimulator*]': ['$(inherited)', 'BPP_STEAM_TEST_CONTROL=$(BPP_STEAM_TEST_CONTROL)'],
    }
    configs = []
    configuration = file('Launcher.xcconfig', 'text.xcconfig')
    for name in ['Debug', 'Release']:
        configs.append(add('config:' + name, 'XCBuildConfiguration', name=name,
                           baseConfigurationReference=configuration,
                           buildSettings={**settings, 'SWIFT_OPTIMIZATION_LEVEL': '-Onone' if name == 'Debug' else '-O',
                                          'GCC_OPTIMIZATION_LEVEL': '0' if name == 'Debug' else 's'}))
    config_list = add('configs', 'XCConfigurationList', buildConfigurations=configs,
                      defaultConfigurationIsVisible=0, defaultConfigurationName='Release')
    product = add('product', 'PBXFileReference', explicitFileType='wrapper.application',
                  path=PRODUCT + '.app', sourceTree='BUILT_PRODUCTS_DIR')
    target = add('app', 'PBXNativeTarget', name=PRODUCT, productName=PRODUCT,
                 productType='com.apple.product-type.application', productReference=product,
                 buildConfigurationList=config_list, buildPhases=[sources, frameworks, resources, embed, package],
                 buildRules=[], dependencies=[])
    products = add('products', 'PBXGroup', name='Products', children=[product], sourceTree='<group>')
    group = add('root', 'PBXGroup', children=[*source_files, file('Resources/Info.plist', 'text.plist.xml'), assets, configuration, framework, products], sourceTree='<group>')
    project_configs = [add('project:' + name, 'XCBuildConfiguration', name=name, buildSettings={})
                       for name in ['Debug', 'Release']]
    project_list = add('project-configs', 'XCConfigurationList', buildConfigurations=project_configs,
                       defaultConfigurationIsVisible=0, defaultConfigurationName='Release')
    project = add('project', 'PBXProject', buildConfigurationList=project_list,
                  compatibilityVersion='Xcode 14.0', developmentRegion='en', knownRegions=['en', 'Base'],
                  mainGroup=group, productRefGroup=products, projectDirPath='', projectRoot='', targets=[target],
                  attributes={'LastUpgradeCheck': '1600'})
    path = ROOT / f'app/{PRODUCT}.xcodeproj'
    path.mkdir(exist_ok=True)
    (path / 'project.pbxproj').write_bytes(plistlib.dumps(dict(archiveVersion='1', classes={}, objectVersion='56', objects=objects, rootObject=project)))
    scheme = ET.Element('Scheme', LastUpgradeVersion='1600', version='1.3')

    def reference(parent):
        ET.SubElement(parent, 'BuildableReference', BuildableIdentifier='primary', BlueprintIdentifier=target,
                      BuildableName=PRODUCT + '.app', BlueprintName=PRODUCT, ReferencedContainer=f'container:{PRODUCT}.xcodeproj')

    build = ET.SubElement(scheme, 'BuildAction', parallelizeBuildables='YES', buildImplicitDependencies='YES')
    entries = ET.SubElement(build, 'BuildActionEntries')
    entry = ET.SubElement(entries, 'BuildActionEntry', **{key: 'YES' for key in ['buildForTesting', 'buildForRunning', 'buildForProfiling', 'buildForArchiving', 'buildForAnalyzing']})
    reference(entry)
    launch = ET.SubElement(scheme, 'LaunchAction', buildConfiguration='Debug',
                           selectedDebuggerIdentifier='Xcode.DebuggerFoundation.Debugger.LLDB',
                           selectedLauncherIdentifier='Xcode.IDEFoundation.Launcher.LLDB', launchStyle='0',
                           useCustomWorkingDirectory='NO', ignoresPersistentStateOnLaunch='NO',
                           debugDocumentVersioning='YES', allowLocationSimulation='YES')
    reference(ET.SubElement(launch, 'BuildableProductRunnable', runnableDebuggingMode='0'))
    ET.SubElement(scheme, 'AnalyzeAction', buildConfiguration='Debug')
    ET.SubElement(scheme, 'ArchiveAction', buildConfiguration='Release', revealArchiveInOrganizer='YES')
    shared = path / 'xcshareddata/xcschemes'
    shared.mkdir(parents=True, exist_ok=True)
    ET.indent(scheme)
    ET.ElementTree(scheme).write(shared / f'{PRODUCT}.xcscheme', encoding='utf-8', xml_declaration=True)
    return path


if __name__ == '__main__':
    print(generate())
