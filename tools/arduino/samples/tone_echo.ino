void setup() { Serial.begin(9600); pinMode(7, OUTPUT); }
void loop() {
  tone(8, 440, 100);
  digitalWrite(7, HIGH);
  delay(200);
  digitalWrite(7, LOW);
  while (Serial.available()) { char c = Serial.read(); if (c >= 'a' && c <= 'z') c -= 32; Serial.write(c); }
  delay(200);
}
