import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { PollPhotoUploadService, UploadState } from '../services/poll-photo-upload.service';

interface UploadItem {
  id: string;
  file: File;
  status: 'queued' | 'uploading' | 'done' | 'error';
  progress: number;
  attempt: number;
  errorMessage?: string;
  photoUrl?: string;
  thumbnailUrl?: string | null;
  subscription?: Subscription;
}

@Component({
  selector: 'app-folder',
  templateUrl: './folder.page.html',
  styleUrls: ['./folder.page.scss'],
  standalone: false,
})
export class FolderPage implements OnInit, OnDestroy {
  public folder!: string;
  public pollId: string = '1';
  public accessToken: string = '';
  public uploads: UploadItem[] = [];

  private activatedRoute = inject(ActivatedRoute);
  private pollPhotoUploadService = inject(PollPhotoUploadService);

  constructor() {}

  ngOnInit() {
    this.folder = this.activatedRoute.snapshot.paramMap.get('id') as string;
  }

  ngOnDestroy(): void {
    for (const item of this.uploads) {
      item.subscription?.unsubscribe();
    }
  }

  public onFileSelectionChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    if (files.length === 0) {
      return;
    }

    const nextItems = files.map((file) => ({
      id: `${file.name}-${file.size}-${Date.now()}-${Math.round(Math.random() * 100000)}`,
      file,
      status: 'queued' as const,
      progress: 0,
      attempt: 0
    }));

    this.uploads = [...this.uploads, ...nextItems].slice(0, 4);
    input.value = '';
  }

  public startUploads(): void {
    for (const item of this.uploads) {
      if (item.status === 'queued' || item.status === 'error') {
        this.uploadSingle(item);
      }
    }
  }

  public retryUpload(item: UploadItem): void {
    this.uploadSingle(item);
  }

  public clearFinished(): void {
    this.uploads = this.uploads.filter((item) => item.status !== 'done');
  }

  public removeUpload(item: UploadItem): void {
    item.subscription?.unsubscribe();
    this.uploads = this.uploads.filter((candidate) => candidate.id !== item.id);
  }

  private uploadSingle(item: UploadItem): void {
    const parsedPollId = Number(this.pollId);
    if (!Number.isInteger(parsedPollId) || parsedPollId <= 0) {
      item.status = 'error';
      item.errorMessage = 'Poll ID jest wymagane.';
      return;
    }

    item.subscription?.unsubscribe();
    item.status = 'uploading';
    item.progress = 0;
    item.errorMessage = undefined;

    item.subscription = this.pollPhotoUploadService
      .uploadWithRetry(parsedPollId, item.file, 2)
      .subscribe((state: UploadState) => {
        item.attempt = state.attempt;
        item.progress = state.progress;

        if (state.status === 'queued') {
          item.status = 'queued';
          return;
        }

        if (state.status === 'in-progress') {
          item.status = 'uploading';
          return;
        }

        if (state.status === 'done') {
          item.status = 'done';
          item.photoUrl = state.photo?.photoUrl;
          item.thumbnailUrl = state.photo?.thumbnailUrl;
          item.errorMessage = undefined;
          return;
        }

        item.status = 'error';
        item.errorMessage = state.errorMessage ?? 'Upload failed.';
      });
  }
}
