
#!/bin/bash

# Test 1: POST /api/users
echo "Test 1: POST /api/users"
response=$(curl -s -w "%{http_code}" -o /tmp/test1_response.json -X POST http://localhost:8080/api/users \
  -H "Content-Type: application/json" \
  -d '{
    "email": "walt@breakingbad.com",
    "password": "123456"
  }')

expected_status=201
actual_status=$(echo "$response" | tail -n1)
if [ "$actual_status" -eq "$expected_status" ]; then
  echo "Status code check: PASSED"
else
  echo "Status code check: FAILED (expected $expected_status, got $actual_status)"
fi

jq '.id == 1 and .email == "walt@breakingbad.com" and .is_chirpy_red == false' /tmp/test1_response.json > /tmp/test1_result.json
if grep -q true /tmp/test1_result.json; then
  echo "JSON content check: PASSED"
else
  echo "JSON content check: FAILED"
fi

# Test 2: POST /api/polka/webhooks (Unauthorized)
echo "Test 2: POST /api/polka/webhooks (Unauthorized)"
response=$(curl -s -w "%{http_code}" -o /dev/null -X POST http://localhost:8080/api/polka/webhooks \
  -H "Content-Type: application/json" \
  -d '{
    "data": {
      "user_id": 1
    },
    "event": "user.upgraded"
  }')

expected_status=401
if [ "$response" -eq "$expected_status" ]; then
  echo "Unauthorized status code check: PASSED"
else
  echo "Unauthorized status code check: FAILED (expected $expected_status, got $response)"
fi

# Test 3: POST /api/polka/webhooks (Authorized)
echo "Test 3: POST /api/polka/webhooks (Authorized)"
response=$(curl -s -w "%{http_code}" -o /dev/null -X POST http://localhost:8080/api/polka/webhooks \
  -H "Content-Type: application/json" \
  -H "Authorization: ApiKey f271c81ff7084ee5b99a5091b42d486e" \
  -d '{
    "data": {
      "user_id": 1
    },
    "event": "user.upgraded"
  }')

expected_status=204
if [ "$response" -eq "$expected_status" ]; then
  echo "Authorized status code check: PASSED"
else
  echo "Authorized status code check: FAILED (expected $expected_status, got $response)"
fi

# Test 4: POST /api/login
echo "Test 4: POST /api/login"
response=$(curl -s -w "%{http_code}" -o /tmp/test4_response.json -X POST http://localhost:8080/api/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "walt@breakingbad.com",
    "password": "123456"
  }')

expected_status=200
actual_status=$(echo "$response" | tail -n1)
if [ "$actual_status" -eq "$expected_status" ]; then
  echo "Status code check: PASSED"
else
  echo "Status code check: FAILED (expected $expected_status, got $actual_status)"
fi

jq '.id == 1 and .email == "walt@breakingbad.com" and .is_chirpy_red == true' /tmp/test4_response.json > /tmp/test4_result.json
if grep -q true /tmp/test4_result.json; then
  echo "JSON content check: PASSED"
else
  echo "JSON content check: FAILED"
fi
