import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind,
} from 'vscode-languageclient/node';

let client: LanguageClient;

function resolveServerPath(context: vscode.ExtensionContext): string {
    const platform = process.platform;
    const rid = platform === 'win32' ? 'win-x64'
               : platform === 'darwin' ? 'osx-arm64'
               : 'linux-x64';
    const exe = platform === 'win32' ? 'DScript.LanguageServer.exe' : 'DScript.LanguageServer';

    // Bundled binary (packaged .vsix)
    const bundled = context.asAbsolutePath(path.join('server', rid, exe));
    if (fs.existsSync(bundled)) return bundled;

    // Development fallback: neighbouring project build output
    return context.asAbsolutePath(
        path.join('..', 'DScript.LanguageServer', 'bin', 'Debug', 'net10.0', exe),
    );
}

export function activate(context: vscode.ExtensionContext): void {
    const serverOptions: ServerOptions = {
        command: resolveServerPath(context),
        transport: TransportKind.stdio,
    };

    const clientOptions: LanguageClientOptions = {
        documentSelector: [{ scheme: 'file', language: 'dscript' }],
        synchronize: {
            fileEvents: vscode.workspace.createFileSystemWatcher('**/*.ds'),
        },
        outputChannelName: 'DScript Language Server',
    };

    client = new LanguageClient(
        'dscript',
        'DScript Language Server',
        serverOptions,
        clientOptions,
    );

    client.start();
    context.subscriptions.push(client);
}

export function deactivate(): Thenable<void> | undefined {
    return client?.stop();
}
