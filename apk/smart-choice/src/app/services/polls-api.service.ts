import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  CastVoteRequest,
  CreatePollRequest,
  PollDto,
  PollPhotoDto,
  PollResultsDto,
  VoteDto,
} from '../models/api.models';

@Injectable({ providedIn: 'root' })
export class PollsApiService {
  constructor(private readonly httpClient: HttpClient) {}

  createPollDraft(request: CreatePollRequest): Observable<PollDto> {
    return this.httpClient.post<PollDto>(`${environment.apiBaseUrl}/api/polls`, request);
  }

  uploadPhoto(pollId: number, file: File): Observable<PollPhotoDto> {
    const formData = new FormData();
    formData.append('file', file, file.name);

    return this.httpClient.post<PollPhotoDto>(`${environment.apiBaseUrl}/api/polls/${pollId}/photos`, formData);
  }

  publishPoll(pollId: number): Observable<PollDto> {
    return this.httpClient.post<PollDto>(`${environment.apiBaseUrl}/api/polls/${pollId}/publish`, {});
  }

  castVote(request: CastVoteRequest): Observable<VoteDto> {
    return this.httpClient.post<VoteDto>(`${environment.apiBaseUrl}/api/votes`, request);
  }

  getResults(pollId: number): Observable<PollResultsDto> {
    return this.httpClient.get<PollResultsDto>(`${environment.apiBaseUrl}/api/polls/${pollId}/results`);
  }
}
