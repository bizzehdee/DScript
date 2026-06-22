import * as path from 'path';
import * as vscode from 'vscode';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind,
} from 'vscode-languageclient/node';

let client: LanguageClient;

export function activate(context: vscode.ExtensionContext): void {
    // Resolve the path to the compiled language server executable.
    // When the extension is published the server binary should be bundled; for
    // development it lives under the neighbouring DScript.LanguageServer project.
    const serverExe = context.asAbsolutePath(
        path.join(
            '..',
            'DScript.LanguageServer',
            'bin',
            'Debug',
            'net10.0',
            process.platform === 'win32'
                ? 'DScript.LanguageServer.exe'
                : 'DScript.LanguageServer',
        ),
    );

    const serverOptions: ServerOptions = {
        command: serverExe,
        transport: TransportKind.stdio,
    };

    const clientOptions: LanguageClientOptions = {
        // Register for .ds files.
        documentSelector: [{ scheme: 'file', language: 'dscript' }],
        synchronize: {
            // Watch for .ds file changes on disk so the server is notified.
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
