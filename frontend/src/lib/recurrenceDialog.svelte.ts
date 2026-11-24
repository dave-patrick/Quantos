export interface RecurrenceDialogRequest {
  value: string | null;
  scheduled?: string | null;
  showPresets?: boolean;
  done: (result: string | null | undefined) => void;
}

export const recurrenceDialog = $state<{ request: RecurrenceDialogRequest | null }>({ request: null });

export function editRecurrence(options: { value?: string | null; scheduled?: string | null; showPresets?: boolean } = {}): Promise<string | null | undefined> {
  recurrenceDialog.request?.done(undefined);
  return new Promise((resolve) => {
    recurrenceDialog.request = {
      value: options.value ?? null,
      scheduled: options.scheduled ?? null,
      showPresets: options.showPresets ?? false,
      done: (result) => {
        recurrenceDialog.request = null;
        resolve(result);
      }
    };
  });
}
