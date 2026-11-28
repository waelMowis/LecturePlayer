// RGB (سالب مشترك) على 9 و 10 و 11 + شاشة سبع قطع (A..G على 2..8)
const byte DIG[10] = {0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F};
void setup() { for (int p = 2; p <= 8; p++) pinMode(p, OUTPUT); Serial.begin(9600); }
void loop() {
  for (int d = 0; d < 10; d++) {
    for (int s = 0; s < 7; s++) digitalWrite(2 + s, (DIG[d] >> s) & 1);
    analogWrite(9, d * 25); analogWrite(10, 255 - d * 25); analogWrite(11, d % 2 ? 255 : 0);
    Serial.println(d);
    delay(300);
  }
}
