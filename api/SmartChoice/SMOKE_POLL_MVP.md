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

1. Zaloguj drugi raz jako inny user (lub użyj tokena guest z `/api/auth/guest`) i zapisz `VOTER_TOKEN`.
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
- body zawiera `photoUrl`, `thumbnailUrl`, `displayOrder`

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
