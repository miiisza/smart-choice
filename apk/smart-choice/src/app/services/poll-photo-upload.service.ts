import {
  HttpClient,
  HttpErrorResponse,
  HttpEvent,
  HttpEventType,
  HttpRequest,
} from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, catchError, defer, map, of, retry, throwError, timer } from 'rxjs';
import { environment } from '../../environments/environment';
import { PollPhotoDto } from '../models/api.models';

export interface UploadState {
  status: 'queued' | 'in-progress' | 'done' | 'error';
  progress: number;
  attempt: number;
  photo?: PollPhotoDto;
  errorMessage?: string;
}

@Injectable({ providedIn: 'root' })
export class PollPhotoUploadService {
  constructor(private readonly httpClient: HttpClient) {}

  uploadWithRetry(pollId: number, file: File, maxRetries: number = 2): Observable<UploadState> {
    let attempt = 0;

    return defer(() => {
      attempt += 1;
      return this.uploadSingleAttempt(pollId, file, attempt);
    }).pipe(
      retry({
        count: Math.max(0, maxRetries),
        delay: (error) => {
          if (!this.isRetryable(error)) {
            return throwError(() => error);
          }

          const delayMs = Math.min(1000 * attempt, 3000);
          return timer(delayMs);
        },
      }),
      catchError((error: unknown) =>
        of<UploadState>({
          status: 'error',
          progress: 0,
          attempt,
          errorMessage: this.toErrorMessage(error),
        })
      )
    );
  }

  private uploadSingleAttempt(
    pollId: number,
    file: File,
    attempt: number
  ): Observable<UploadState> {
    const formData = new FormData();
    formData.append('file', file, file.name);

    const request = new HttpRequest<PollPhotoDto>(
      'POST',
      `${environment.apiBaseUrl}/api/polls/${pollId}/photos`,
      formData,
      {
        reportProgress: true,
        responseType: 'json',
      }
    );

    return this.httpClient
      .request<PollPhotoDto>(request)
      .pipe(map((event: HttpEvent<PollPhotoDto>) => this.toUploadState(event, attempt)));
  }

  private toUploadState(event: HttpEvent<PollPhotoDto>, attempt: number): UploadState {
    if (event.type === HttpEventType.UploadProgress) {
      const total = event.total ?? 1;
      const progress = Math.min(100, Math.round((event.loaded * 100) / total));
      return {
        status: 'in-progress',
        progress,
        attempt,
      };
    }

    if (event.type === HttpEventType.Response) {
      return {
        status: 'done',
        progress: 100,
        attempt,
        photo: event.body ?? undefined,
      };
    }

    return {
      status: 'in-progress',
      progress: 0,
      attempt,
    };
  }

  private isRetryable(error: unknown): boolean {
    if (!(error instanceof HttpErrorResponse)) {
      return false;
    }

    return error.status === 0 || error.status >= 500 || error.status === 429;
  }

  private toErrorMessage(error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) {
      return 'Upload failed.';
    }

    if (error.error?.detail) {
      return error.error.detail;
    }

    if (error.error?.title) {
      return error.error.title;
    }

    return `Upload failed (HTTP ${error.status || 'network'}).`;
  }
}
