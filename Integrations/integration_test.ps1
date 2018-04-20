$bidEndpoint = "http://localhost:80/api/orders/bid"
$askEndpoint = "http://localhost:80/api/orders/ask"
$ordersEndpoint = "http://localhost:80/api/orders"
$usersEndpoint = "http://localhost:8080/api/users"

# Must be run on a clean cluster to ensure the bid and ask are matched

$orders = Invoke-RestMethod -Method Get -Uri $ordersEndpoint 
$startAskCount = $orders.asksCount
$startBidCount = $orders.bidsCount

Write-Host "Starting Ask Count: $startAskCount"
Write-Host "Starting Bid Count: $startBidCount"

# Create buyer
Write-Host "Creating buyer"
$buyer = @{
    'Balance' = 100
    'Quantity' = 100
    'Username' = "buyer"
} | ConvertTo-Json
$buyerId = Invoke-RestMethod -Method Post -Uri $usersEndpoint -Body $buyer -ContentType "application/json" 
if ($buyerId -eq "")
{
    Write-Host "failed to create buyer"
    exit
}

# Create seller
Write-Host "Creating seller"
$seller = @{
    'balance' = 100
    'quantity' = 100
    'username' = "seller"
} | ConvertTo-Json
$sellerId = Invoke-RestMethod -Method Post -Uri $usersEndpoint -Body $seller -ContentType "application/json" 
if ($sellerId -eq "")
{
    Write-Host "failed to create seller"
    exit
}

# Create bid
Write-Host "Creating bid"
$bid = @{
    'value' = 100
    'quantity' = 100
    'userId' = $buyerId
} | ConvertTo-Json
$bidId = Invoke-RestMethod -Method Post -Uri $bidEndpoint -Body $bid -ContentType "application/json" 
if ($bidId -eq "")
{
    Write-Host "failed to create bid"
    exit
}

# Create ask
Write-Host "Creating ask"
$ask = @{
    'value' = 100
    'quantity' = 100
    'userId' = $sellerId
} | ConvertTo-Json
$askId = Invoke-RestMethod -Method Post -Uri $askEndpoint -Body $ask -ContentType "application/json" 
if ($askId -eq "")
{
    Write-Host "Failed to create ask"
    exit
}

# Check orders created
$orders = Invoke-RestMethod -Method Get -Uri $ordersEndpoint 
$afterAskCount = $orders.asksCount
$afterBidCount = $orders.bidsCount

# Check ask count has incremented
$expectedAskCount = $startAskCount + 1
if (!($afterAskCount -eq $expectedAskCount))
{
    Write-Host "Expected ask count $expectedAskCount, got $afterAskCount"
    exit
}

# Check bid count has incremented
$expectedBidCount = $startBidCount + 1
if (!($afterBidCount -eq $expectedBidCount))
{
    Write-Host "Expected bid count $expectedBidCount, got $afterBidCount"
    exit
}

# Wait...
$waitPeriod = 15
Write-Host "Waiting for $waitPeriod seconds to allow transfer to be processed..."
Start-Sleep -Seconds $waitPeriod

# Check seller updated
$afterSeller = Invoke-RestMethod -Method Get -Uri "$usersEndpoint/$sellerId"
$afterSellerTransfersCount = $afterSeller.transfers.Count
$expectedSellerTransfersCount = $seller.transfers.Count + 1
if(!($afterSellerTransfersCount -eq $expectedSellerTransfersCount))
{
    Write-Host "expected seller transfers count $expectedSellerTransfersCount, got $afterSellerTransfersCount"
    exit
}

# Check buyer updated
$afterBuyer = Invoke-RestMethod -Method Get -Uri "$usersEndpoint/$sellerId"
$afterBuyerTransfersCount = $afterBuyer.transfers.Count
$expectedBuyerTransfersCount = $buyer.transfers.Count + 1
if(!($afterBuyerTransfersCount -eq $expectedBuyerTransfersCount))
{
    Write-Host "expected seller transfers count $expectedBuyerTransfersCount, got $afterBuyerTransfersCount"
    exit
}

Write-Host "Successfully ran integration test"


