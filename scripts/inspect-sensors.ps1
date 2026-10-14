$ErrorActionPreference = 'Continue'
foreach ($name in @('Accelerometer','Gyrometer','LightSensor','Inclinometer','Compass','SimpleOrientationSensor','ProximitySensor')) {
    try {
        $sensorType = [Type]::GetType("Windows.Devices.Sensors.$name, Windows, ContentType=WindowsRuntime")
        if ($null -eq $sensorType) { Write-Output "$name : API unavailable"; continue }
        $method = $sensorType.GetMethod('GetDefault', [Type[]]@())
        if ($null -eq $method) { Write-Output "$name : no default-device API"; continue }
        $sensor = $method.Invoke($null, @())
        Write-Output ("{0} : {1}" -f $name, ($null -ne $sensor))
    } catch { Write-Output ("{0} : {1}" -f $name, $_.Exception.GetBaseException().Message) }
}
