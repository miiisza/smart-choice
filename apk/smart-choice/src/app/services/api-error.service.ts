import { Injectable } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiProblemError } from '../interceptors/problem-details.interceptor';

@Injectable({ providedIn: 'root' })
export class ApiErrorService {
  toMessage(error: unknown, fallbackMessage: string = 'Wystąpił błąd.'): string {
    if (error instanceof ApiProblemError) {
      return error.message;
    }

    if (error instanceof HttpErrorResponse) {
      if (typeof error.error === 'string' && error.error.trim().length > 0) {
        return error.error;
      }

      if (error.status > 0) {
        return `Request failed (HTTP ${error.status}).`;
      }

      return 'Brak połączenia z siecią. Sprawdź internet i spróbuj ponownie.';
    }

    if (error instanceof Error && error.message.trim().length > 0) {
      return error.message;
    }

    return fallbackMessage;
  }
}
