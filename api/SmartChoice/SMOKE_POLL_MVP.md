# Poll MVP Smoke Tests

Zakładaj uruchomione API pod `http://localhost:5148`.

## Precondition

1. Zarejestruj i zaloguj użytkownika:

```bash
curl -s -X POST http://localhost:5148/api/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"email":"poll.qa@example.com","password":"QaPassword123!"}'
```

2. Zapisz `accessToken` jako `USER_TOKEN`.

## 1) Create poll draft -> 201 (`status=Draft`)

```bash
curl -i -X POST http://localhost:5148/api/polls \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer <USER_TOKEN>" \
  -d '{"question":"Które zdjęcie wybrać?","photoUrls":["https://img.example/1.jpg"],"latitude":52.2297,"longitude":21.0122,"radiusMeters":5000,"startsAt":"2026-03-01T00:00:00Z","endsAt":"2026-03-10T00:00:00Z"}'
```

Oczekiwane:
- `201 Created`
- body zawiera `id` i `status = 0` (`Draft`)

## 2) Publish draft z 1 zdjęciem -> 409 (reguła domenowa)

1. Weź `id` z kroku 1 jako `DRAFT_POLL_ID`.
2. Wywołaj:

```bash
curl -i -X POST http://localhost:5148/api/polls/<DRAFT_POLL_ID>/publish \
  -H "Authorization: Bearer <USER_TOKEN>"
```

Oczekiwane:
- `409 Conflict`
- `title = Poll publish rejected`
- `detail` mówi o min. 2 zdjęciach

## 3) Publish poprawnego draftu -> 200 (`status=Open`)

1. Utwórz drugi draft z min. 2 zdjęciami:

```bash
curl -i -X POST http://localhost:5148/api/polls \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer <USER_TOKEN>" \
  -d '{"question":"Finał","photoUrls":["https://img.example/a.jpg","https://img.example/b.jpg"],"latitude":52.2297,"longitude":21.0122,"radiusMeters":5000}'
```

2. Weź `id` jako `OPENABLE_POLL_ID` i opublikuj:

```bash
curl -i -X POST http://localhost:5148/api/polls/<OPENABLE_POLL_ID>/publish \
  -H "Authorization: Bearer <USER_TOKEN>"
```

Oczekiwane:
- `200 OK`
- `status = 1` (`Open`)

## 4) Vote duplicate block -> 201, potem 409

1. Zaloguj drugi raz jako inny user albo pobierz token guest dla `OPENABLE_POLL_ID` i zapisz `VOTER_TOKEN`:

```bash
curl -s -X POST http://localhost:5148/api/auth/guest \
  -H 'Content-Type: application/json' \
  -d '{"inviteCode":"DEV2026","pollId":<OPENABLE_POLL_ID>}'
```

2. Pobierz `pollPhotoId` z opublikowanego polla (`OPENABLE_POLL_ID`) przez feed:

```bash
curl -s "http://localhost:5148/api/polls/feed?lat=52.2297&lng=21.0122&radius=5000&page=1"
```

3. Oddaj głos:

```bash
curl -i -X POST http://localhost:5148/api/votes \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer <VOTER_TOKEN>" \
  -d '{"pollId":<OPENABLE_POLL_ID>,"pollPhotoId":<POLL_PHOTO_ID>}'
```

4. Powtórz identyczny request.

Oczekiwane:
- pierwszy request: `201 Created`
- drugi request: `409 Conflict` (`Duplicate vote`)

## 5) Close + results (winner + procenty)

1. Zamknij poll:

```bash
curl -i -X POST http://localhost:5148/api/polls/<OPENABLE_POLL_ID>/close \
  -H "Authorization: Bearer <USER_TOKEN>"
```

2. Pobierz wyniki:

```bash
curl -i http://localhost:5148/api/polls/<OPENABLE_POLL_ID>/results
```

Oczekiwane:
- close: `200 OK`, `status = 2` (`Closed`)
- results: `200 OK`, body zawiera:
  - `totalVotes`
  - `winner` (dla `totalVotes > 0`)
  - `options[]` z `voteCount` i `percentage` dla każdego zdjęcia

## 6) Upload zdjęcia do drafta (S3/MinIO) + blokada po publish

1. Utwórz draft bez zdjęć:

```bash
curl -i -X POST http://localhost:5148/api/polls \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer <USER_TOKEN>" \
  -d '{"question":"Upload flow","photoUrls":[],"latitude":52.2297,"longitude":21.0122,"radiusMeters":5000}'
```

2. Weź `id` jako `UPLOAD_POLL_ID` i wyślij 2 zdjęcia:

```bash
curl -i -X POST http://localhost:5148/api/polls/<UPLOAD_POLL_ID>/photos \
  -H "Authorization: Bearer <USER_TOKEN>" \
  -F "file=@/absolute/path/to/photo1.jpg"

curl -i -X POST http://localhost:5148/api/polls/<UPLOAD_POLL_ID>/photos \
  -H "Authorization: Bearer <USER_TOKEN>" \
  -F "file=@/absolute/path/to/photo2.jpg"
```

Oczekiwane:
- `201 Created` dla obu requestów
- body zawiera signed URL-e: `displayUrl`, `thumbUrl` (oraz legacy `photoUrl`, `thumbnailUrl`)

3. Opublikuj poll i spróbuj upload po publikacji:

```bash
curl -i -X POST http://localhost:5148/api/polls/<UPLOAD_POLL_ID>/publish \
  -H "Authorization: Bearer <USER_TOKEN>"

curl -i -X POST http://localhost:5148/api/polls/<UPLOAD_POLL_ID>/photos \
  -H "Authorization: Bearer <USER_TOKEN>" \
  -F "file=@/absolute/path/to/photo3.jpg"
```

Oczekiwane:
- publish: `200 OK`
- upload po publish: `409 Conflict` (`Photos can only be uploaded for draft polls.`)

4. Sprawdź dostęp cudzym tokenem:

```bash
curl -i -X POST http://localhost:5148/api/polls/<UPLOAD_POLL_ID>/photos \
  -H "Authorization: Bearer <OTHER_USER_TOKEN>" \
  -F "file=@/absolute/path/to/photo4.jpg"
```

Oczekiwane:
- `403 Forbidden` (`Only the poll author can upload photos for this poll.`)

## 7) Rate limiting `POST /api/polls` (create) -> 429

W 60 sekundach wywołaj endpoint więcej niż 5 razy tym samym tokenem:

```bash
for i in $(seq 1 6); do
  curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5148/api/polls \
    -H 'Content-Type: application/json' \
    -H "Authorization: Bearer <USER_TOKEN>" \
    -d "{\"question\":\"Load test $i\",\"photoUrls\":[\"https://img.example/a.jpg\",\"https://img.example/b.jpg\"],\"latitude\":52.2297,\"longitude\":21.0122,\"radiusMeters\":5000}"
done
```

Oczekiwane:
- pierwsze requesty: `201` / `400` (zależnie od payloadu)
- po przekroczeniu limitu: `429 Too Many Requests` + `application/problem+json`

## 8) Rate limiting `POST /api/votes` -> 429

W 60 sekundach wywołaj `POST /api/votes` więcej niż 12 razy tym samym tokenem:

```bash
for i in $(seq 1 13); do
  curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5148/api/votes \
    -H 'Content-Type: application/json' \
    -H "Authorization: Bearer <VOTER_TOKEN>" \
    -d '{"pollId":<OPENABLE_POLL_ID>,"pollPhotoId":<POLL_PHOTO_ID>}'
done
```

Oczekiwane:
- po przekroczeniu limitu: `429 Too Many Requests`
- body błędu jest w `ProblemDetails`

## 9) ProblemDetails dla statusów domenowych

Sprawdź przynajmniej 1 odpowiedź dla każdego statusu: `400`, `401`, `403`, `404`, `409`, `429`.

Oczekiwane:
- `Content-Type: application/problem+json`
- spójne pola: `type`, `title`, `status`, `detail`, `instance`, `traceId`
- dla `400` walidacyjnych obecne `errors`

## 10) Logi kluczowych zdarzeń

Podczas testów sprawdź log backendu (`docker logs`, konsola API):
- issue guest token: sukces + przypadki odrzucone
- create poll draft: sukces + walidacje/odrzucenia
- cast vote: sukces + duplicate + odrzucone tokeny
- rate limit reject (429)
