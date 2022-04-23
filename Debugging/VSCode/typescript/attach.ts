'use strict';

import {debug, workspace, commands, window, ExtensionContext, QuickPickItem, QuickPickOptions, DebugConfiguration, DebugConfigurationProvider, WorkspaceFolder, CancellationToken, ProviderResult} from 'vscode';
import {DebugProtocol} from 'vscode-debugprotocol';
import * as nls from 'vscode-nls';
import {exec} from 'child_process';
import { Exceptions, ExceptionConfigurations } from './exceptions';

const localize = nls.config({locale: process.env.VSCODE_NLS_CONFIG})();
var exceptions;

const DEFAULT_EXCEPTIONS: ExceptionConfigurations = {
    "System.Exception": "never",
    "System.SystemException": "never",
    "System.ArithmeticException": "never",
    "System.ArrayTypeMismatchException": "never",
    "System.DivideByZeroException": "never",
    "System.IndexOutOfRangeException": "never",
    "System.InvalidCastException": "never",
    "System.NullReferenceException": "never",
    "System.OutOfMemoryException": "never",
    "System.OverflowException": "never",
    "System.StackOverflowException": "never",
    "System.TypeInitializationException": "never"
};

export function activate(context: ExtensionContext) {
    exceptions = new Exceptions(DEFAULT_EXCEPTIONS);
    window.registerTreeDataProvider("exceptions", exceptions);
    context.subscriptions.push(commands.registerCommand('exceptions.always', exception => exceptions.always(exception)));
    context.subscriptions.push(commands.registerCommand('exceptions.never', exception => exceptions.never(exception)));
    context.subscriptions.push(commands.registerCommand('exceptions.addEntry', t => exceptions.addEntry(t)));
	context.subscriptions.push(commands.registerCommand('attach.attachToDebugger', config => startSession(context, config)));
}

export function deactivate() {
}

async function startSession(context: ExtensionContext, config: any) {
    let response = await debug.startDebugging(undefined, config);
    console.log("debug ended: " + response);
}