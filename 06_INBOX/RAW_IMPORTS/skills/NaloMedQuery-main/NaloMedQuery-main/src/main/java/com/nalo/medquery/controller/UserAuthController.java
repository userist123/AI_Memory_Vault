package com.nalo.medquery.controller;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.http.ResponseEntity;
import org.springframework.security.authentication.AuthenticationManager;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import com.nalo.medquery.domain.entity.UserEntity;
import com.nalo.medquery.domain.model.auth.AuthData;
import com.nalo.medquery.infra.security.DataJWT;
import com.nalo.medquery.infra.security.TokenService;

import jakarta.validation.Valid;

import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;

@RestController
@RequestMapping("/login")
public class UserAuthController {

    @Autowired
    private AuthenticationManager manager;

    @Autowired
    private TokenService tokenService;

    @PostMapping
    public ResponseEntity<DataJWT> login( @RequestBody @Valid AuthData data) {
        var authenticationToken = new UsernamePasswordAuthenticationToken(data.login(), data.senha());
        var authentication = manager.authenticate(authenticationToken);
        var token = tokenService.generateToken((UserEntity) authentication.getPrincipal());
        
        return ResponseEntity.ok(new DataJWT(token));
    }

}
