export interface ProductionProcessContext {
  processId: string;
  processCode: string;
  processName: string;
}

export interface ProductionDeviceContext {
  deviceId: string;
  deviceCode: string;
  deviceName: string;
  processId: string;
  processCode: string;
  processName: string;
}

export interface ProductionContext {
  processId: string;
  processCode: string;
  processName: string;
  deviceId: string;
  deviceCode: string;
  deviceName: string;
}

export type ProductionContextStatus = 'idle' | 'loading' | 'ready' | 'error';

export type ProductionContextState =
  | 'loading'
  | 'error'
  | 'no-authorized-devices'
  | 'select-process'
  | 'no-process-devices'
  | 'select-device'
  | 'ready';
