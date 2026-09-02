package com.nalo.medquery.infra.exception;

import org.springframework.http.ResponseEntity;
import org.springframework.validation.FieldError;
import org.springframework.web.bind.MethodArgumentNotValidException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;

import jakarta.persistence.EntityNotFoundException;

@RestControllerAdvice
public class ExceptionsHandler {

    //404
    @ExceptionHandler(EntityNotFoundException.class)
    public ResponseEntity handleNotFound(){
        return ResponseEntity.notFound().build();
    }

    //400
    @ExceptionHandler(MethodArgumentNotValidException.class)
    public ResponseEntity handleErrorBadRequest(MethodArgumentNotValidException exc){
        var errors = exc.getFieldErrors();
        return ResponseEntity.badRequest().body(errors.stream().map(ErrorDataDto::new).toList());
    }

    @ExceptionHandler(IllegalArgumentException.class)
    public ResponseEntity<String> handleExceptionFromValidation(IllegalArgumentException exc){
        return ResponseEntity.badRequest().body(exc.getMessage());
    }

    private record ErrorDataDto(String campo, String messagem){
        public ErrorDataDto(FieldError err){
            this(err.getField(), err.getDefaultMessage());
        }
    }
}
