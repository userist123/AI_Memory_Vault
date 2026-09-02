package com.nalo.medquery.domain.repository;

import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.security.core.userdetails.UserDetails;

import com.nalo.medquery.domain.entity.UserEntity;

public interface IUserRepository extends JpaRepository<UserEntity, Long>{

    UserDetails findByLogin(String login);
    
}
