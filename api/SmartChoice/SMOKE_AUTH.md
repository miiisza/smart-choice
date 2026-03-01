# Auth Smoke Tests

Zakładaj uruchomione API pod `http://localhost:5148`.

## 1) Rejestracja -> 201

```bash
curl -i -X POST http://localhost:5148/api/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"email":"qa1@example.com","password":"QaPassword123!"}'
```

Oczekiwane:
- `201 Created`
- body zawiera `accessToken`, `refreshToken`, `accessTokenExpiresAt`, `refreshTokenExpiresAt`

## 2) Login poprawny -> 200

```bash
curl -i -X POST http://localhost:5148/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"qa1@example.com","password":"QaPassword123!"}'
```

Oczekiwane:
- `200 OK`
- body zawiera nową parę tokenów

## 3) Login błędne hasło -> 401 ProblemDetails

```bash
curl -i -X POST http://localhost:5148/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"qa1@example.com","password":"bad-password"}'
```

Oczekiwane:
- `401 Unauthorized`
- `application/problem+json` z `title=Unauthorized`

## 4) Refresh -> 200

1. Weź `refreshToken` z kroku 1 lub 2.
2. Wywołaj:

```bash
curl -i -X POST http://localhost:5148/api/auth/refresh \
  -H 'Content-Type: application/json' \
  -d '{"refreshToken":"<REFRESH_TOKEN>"}'
```

Oczekiwane:
- `200 OK`
- nowy `accessToken` i `refreshToken`

## 5) Guest token z invite -> 200

```bash
curl -i -X POST http://localhost:5148/api/auth/guest \
  -H 'Content-Type: application/json' \
  -d '{"inviteCode":"DEV2026"}'
```

Oczekiwane:
- `200 OK`
- body zawiera `guestToken` (JWT) i `expiresAt`

## 6) Głosowanie guest: pierwszy głos 201, drugi na ten sam poll 409

1. Pobierz `guestToken` z kroku 5.
2. Pobierz poll i zdjęcie z feed:

```bash
curl -s "http://localhost:5148/api/polls/feed?lat=52.2297&lng=21.0122&radius=5000&page=1"
```

3. Oddaj głos:

```bash
curl -i -X POST http://localhost:5148/api/votes \
  -H 'Content-Type: application/json' \
  -H 'Authorization: Bearer <GUEST_TOKEN>' \
  -d '{"pollId":1,"pollPhotoId":1}'
```

4. Powtórz ten sam request dla tego samego `pollId`.

Oczekiwane:
- pierwszy request: `201 Created`
- drugi request: `409 Conflict` (`Duplicate vote`)

## 7) Autoryzacja endpointów chronionych

```bash
curl -i -X POST http://localhost:5148/api/polls \
  -H 'Content-Type: application/json' \
  -d '{"question":"Czy działa?","photoUrls":["https://a","https://b"],"latitude":52.2297,"longitude":21.0122,"radiusMeters":5000}'
```

Oczekiwane:
- bez tokena: `401 Unauthorized` (`application/problem+json`)
- z JWT usera: `201 Created`
