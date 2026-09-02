ALTER TABLE doctors ADD active TINYINT;
UPDATE doctors SET active = 1;