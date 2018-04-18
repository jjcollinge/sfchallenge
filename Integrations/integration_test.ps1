$exchangeEndpoint = "http://localhost:80/api/exchange"
$usersEndpoint = "http://localhost:8080/api/users"

# Create buyer
$buyer = @{
    'balance' = 100
    'quantity' = 10
    'username' = "buyer"
}
$res = Invoke-WebRequest -Method Post -Uri $usersEndpoint -Body $buyer -Verbose